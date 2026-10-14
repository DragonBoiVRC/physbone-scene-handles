#if PBHANDLES_VRCSDK_PRESENT
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Runs VRChat's own PhysBone solver in edit mode, so a change to Pull or Spring can be
    // watched settling instead of guessed at.
    //
    // How it works, and why it is the SDK's solver rather than our own:
    //
    //   Re-implementing PhysBone physics would produce something that looks plausible and is
    //   wrong in detail - and a preview you can't trust is worse than no preview, because it
    //   invites tuning against the wrong behaviour. VRChat's solver is reachable from editor
    //   code, so the preview drives the real thing:
    //
    //     * PhysBoneManager is a plain MonoBehaviour with public Init/AddPhysBone/AddCollider.
    //       We create one on a hidden GameObject and register the components ourselves.
    //     * Components normally self-register in Start()/OnEnable(), which never run in edit
    //       mode. Their guard is `PhysBoneManager.Inst.IsSDK`, and self-registration here would
    //       mean a component registering twice, so IsSDK stays false and registration is ours.
    //     * Each component needs a chainId before AddPhysBone will accept it (AddPhysBone
    //       early-outs on ChainId.Null) and needs InitTransforms() to have populated its bone
    //       list. Both are arranged below.
    //     * Stepping is VRCDynamicsScheduler.PreScheduleDynamics(finalizeImmediately: true),
    //       which schedules the solve and blocks until it is done. ScheduleExecutionJob itself
    //       is internal, so this public entry point is also the only supported way in.
    //     * PhysBoneManager.DisableTiming + DebugTimeElapsed replace Time-based accumulation
    //       with a fixed per-step delta. That is what makes stepping deterministic and makes a
    //       refresh-rate choice (72/90/120/144 Hz) mean something, rather than the preview
    //       running at whatever rate the editor happens to repaint.
    //
    // The solver writes to real scene transforms, so PhysBonePreviewRestPose snapshots
    // everything before the first step and restores it on stop. Stop is wired to assembly
    // reload, play-mode entry and scene change as well as the user's button, because any of
    // those can happen mid-preview and all of them would otherwise leave bones displaced.
    internal static class PhysBonePreview
    {
        // One step of real time at 60 Hz. The solver's own internal cadence is 1/60 (see
        // UpdateRootsJob), so this is the step size at which one call equals one solver
        // iteration - the refresh-rate setting scales away from it.
        private const float BaseStep = 1f / 60f;

        private static GameObject _host;
        private static PhysBoneManager _manager;
        private static readonly PhysBonePreviewRestPose RestPose = new PhysBonePreviewRestPose();
        private static readonly List<VRCPhysBone> _bones = new List<VRCPhysBone>();
        private static readonly List<VRCPhysBoneCollider> _colliders = new List<VRCPhysBoneCollider>();

        private static bool _running;
        private static bool _paused;
        private static double _lastEditorTime;
        private static float _accumulator;
        private static bool _prevDisableTiming;
        private static float _prevDebugTimeElapsed;
        private static PhysBoneManager _prevInst;
        private static bool _tookOverInst;

        internal static bool IsRunning => _running;
        internal static bool IsPaused => _paused;
        internal static float SimulatedSeconds { get; private set; }
        internal static int StepCount { get; private set; }

        // Hz the preview simulates at. VRChat steps PhysBones at a fixed 60 Hz internally; a
        // higher display rate means smaller real-time slices per solver step, which is what
        // makes a bone feel different on a 144 Hz headset than a 72 Hz one.
        internal static int RefreshRate = 90;

        internal static IReadOnlyList<VRCPhysBone> PreviewedBones => _bones;

        // The live manager, for the grab layer - grabs have to go through the same manager the
        // chains are registered with, and there is no way to find it back from a component.
        internal static PhysBoneManager Manager => _manager;

        internal static event Action StateChanged;

        internal static void Start(IEnumerable<VRCPhysBone> bones)
        {
            if (_running)
                Stop();

            _bones.Clear();
            _colliders.Clear();

            foreach (VRCPhysBone pb in bones)
            {
                if (pb != null && !_bones.Contains(pb))
                    _bones.Add(pb);
            }
            if (_bones.Count == 0)
                return;

            CollectColliders();
            CaptureRestPose();

            if (!CreateManager())
            {
                RestPose.Clear();
                _bones.Clear();
                _colliders.Clear();
                return;
            }

            Register();

            ArmTestMotion();

            if (PhysBoneClipPlayback.HasClip && _bones.Count > 0)
                PhysBoneClipPlayback.Begin(AvatarScopeUtil.ScopeRootOf(_bones[0].transform));

            _running = true;
            _paused = false;
            SimulatedSeconds = 0f;
            StepCount = 0;
            _accumulator = 0f;
            _lastEditorTime = EditorApplication.timeSinceStartup;

            EditorApplication.update += OnEditorUpdate;
            StateChanged?.Invoke();
        }

        internal static void Stop()
        {
            if (!_running && _host == null)
                return;

            EditorApplication.update -= OnEditorUpdate;

            PhysBoneTestMotion.Disarm();

            // Before the rest-pose restore: leaving AnimationMode hands the animated bones back
            // to their pre-sampling values, and the snapshot restore then runs on top of that.
            PhysBoneClipPlayback.Stop();

            // Grabs and poses first: both are registered against chains inside the manager, and
            // a reused (not-ours) manager would otherwise keep holding them after we leave.
            PhysBonePreviewGrab.ReleaseAll(_manager);
            PhysBonePreviewGrab.ClearPoses(_manager, _bones);

            // Before DestroyManager, so the collider is taken out of the components' lists while
            // those components are still the ones the manager knows about.
            PhysBonePreviewTestCollider.Despawn(_manager, _bones);

            // Order matters: the solver has to be torn down before the transforms are put
            // back, or a still-live job can overwrite the restored pose.
            DestroyManager();
            RestPose.Restore();
            RestPose.Clear();

            PhysBonePreviewTrail.Clear();
            _baseline.Clear();

            _bones.Clear();
            _colliders.Clear();
            _running = false;
            _paused = false;
            SimulatedSeconds = 0f;
            StepCount = 0;

            SceneView.RepaintAll();
            StateChanged?.Invoke();
        }

        internal static void TogglePause()
        {
            if (!_running) return;
            _paused = !_paused;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            _accumulator = 0f;
            StateChanged?.Invoke();
        }

        // Advance exactly one solver iteration. Only meaningful while paused, which is the
        // point: frame-stepping a settling bone is how you see where it overshoots.
        internal static void StepOnce()
        {
            if (!_running) return;
            Step(BaseStep);
            SceneView.RepaintAll();
        }

        // Put every previewed bone back to its rest pose without stopping, so a test motion can
        // be re-run from a known start. The solver keeps running and re-settles from rest.
        internal static void ResetToRest()
        {
            if (!_running) return;
            PhysBonePreviewGrab.ReleaseAll(_manager);
            PhysBonePreviewGrab.ClearPoses(_manager, _bones);
            RestPose.Restore();
            PhysBoneTestMotion.Reset();
            PhysBoneClipPlayback.Reset();
            PhysBonePreviewTrail.Clear();
            SimulatedSeconds = 0f;
            StepCount = 0;
            SceneView.RepaintAll();
        }

        // Freeze the pose the bones are in right now and treat it as the thing to compare
        // against, without it becoming what Stop() restores.
        //
        // The use is comparison: get a chain settled under a gust, freeze it, change Pull, and
        // the frozen outline stays on screen next to the new behaviour. Keeping the restore
        // snapshot separate is the important part - a "freeze" that overwrote the rest pose would
        // mean stopping the preview left the avatar in a swung pose, which is the one outcome
        // this whole subsystem exists to prevent.
        private static readonly List<Vector3> _baseline = new List<Vector3>();

        internal static bool HasBaseline => _baseline.Count > 0;

        internal static void CaptureBaseline()
        {
            _baseline.Clear();
            if (!_running)
                return;

            for (int i = 0; i < _bones.Count; i++)
            {
                VRCPhysBone pb = _bones[i];
                if (pb == null || pb.bones == null) continue;
                for (int b = 0; b < pb.bones.Count; b++)
                {
                    Transform t = pb.bones[b].transform;
                    if (t != null)
                        _baseline.Add(t.position);
                }
            }
            SceneView.RepaintAll();
        }

        internal static void ClearBaseline()
        {
            _baseline.Clear();
            SceneView.RepaintAll();
        }

        internal static void DrawBaseline()
        {
            if (_baseline.Count < 2)
                return;

            Color prev = Handles.color;
            Handles.color = new Color(1f, 1f, 1f, 0.35f);
            for (int i = 0; i < _baseline.Count; i++)
            {
                float size = HandleUtility.GetHandleSize(_baseline[i]) * 0.03f;
                Handles.SphereHandleCap(0, _baseline[i], Quaternion.identity, size, EventType.Repaint);
            }
            Handles.color = prev;
        }

        private static void CollectColliders()
        {
            foreach (VRCPhysBone pb in _bones)
            {
                if (pb.colliders == null) continue;
                foreach (VRCPhysBoneColliderBase c in pb.colliders)
                {
                    VRCPhysBoneCollider col = c as VRCPhysBoneCollider;
                    if (col != null && !_colliders.Contains(col))
                        _colliders.Add(col);
                }
            }
        }

        private static void CaptureRestPose()
        {
            // Snapshot from the avatar root, not the PhysBone root: test motions translate the
            // whole avatar, and immobile/world-space settings read the root's movement. Capture
            // has to cover anything the preview can move.
            HashSet<Transform> roots = new HashSet<Transform>();
            foreach (VRCPhysBone pb in _bones)
                roots.Add(AvatarScopeUtil.ScopeRootOf(pb.transform));
            foreach (VRCPhysBoneCollider col in _colliders)
                roots.Add(AvatarScopeUtil.ScopeRootOf(col.transform));

            foreach (Transform root in roots)
                RestPose.Capture(root);
        }

        // Test motions move the avatar root, which is the same transform the rest-pose
        // snapshot covers - so an armed motion is undone by Stop() like everything else.
        private static void ArmTestMotion()
        {
            HashSet<Transform> roots = new HashSet<Transform>();
            foreach (VRCPhysBone pb in _bones)
                roots.Add(AvatarScopeUtil.ScopeRootOf(pb.transform));

            PhysBoneTestMotion.Arm(roots);
        }

        // Re-arm after the selection changes which avatars are previewed, or after the user
        // picks a different motion while running.
        internal static void RefreshTestMotion()
        {
            if (!_running) return;
            PhysBoneTestMotion.Disarm();
            RestPose.Restore();
            ArmTestMotion();
        }

        // Re-enter clip sampling after the clip was swapped mid-preview. Stop first so the
        // previous clip's bones are handed back before the new one starts writing to them.
        internal static void RefreshClipPlayback()
        {
            if (!_running) return;
            PhysBoneClipPlayback.Stop();
            if (PhysBoneClipPlayback.HasClip && _bones.Count > 0)
                PhysBoneClipPlayback.Begin(AvatarScopeUtil.ScopeRootOf(_bones[0].transform));
        }

        // --- solver lifecycle -------------------------------------------------------------

        private static bool CreateManager()
        {
            // If a manager already exists (the SDK's own, left over from a play session, or
            // another tool's) reuse it rather than creating a second one.
            if (PhysBoneManager.Inst != null)
            {
                _manager = PhysBoneManager.Inst;
                _host = null;
            }
            else
            {
                _host = new GameObject("PhysBone Preview (temporary)")
                {
                    // Not saved, not shown, and destroyed on stop. A stray manager object
                    // committed into someone's scene would be a real mess to explain.
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _manager = _host.AddComponent<PhysBoneManager>();

                // IsSDK false on purpose. It is the flag components check in Init() before
                // self-registering; leaving it false keeps registration ours alone, so a
                // component can't end up added twice.
                _manager.IsSDK = false;

                // PhysBoneManager has no [ExecuteAlways], so Awake does NOT run on
                // AddComponent in edit mode. Init() is therefore ours to call, not a
                // belt-and-braces repeat of what Awake already did.
                _manager.Init();
            }

            if (_manager == null)
                return false;

            // The step that makes any of this move.
            //
            // PreScheduleDynamics does not take a manager - it reads the static
            // PhysBoneManager.Inst and skips the PhysBone pass entirely when that is null:
            //
            //     if (PhysBoneManager.Inst != null)
            //         dependsOn = PhysBoneManager.Inst.ScheduleExecutionJob(dependsOn);
            //
            // Inst is assigned in Awake, which (no [ExecuteAlways]) never runs in edit mode.
            // So a manager that is constructed, Init()ed and registered with is still never
            // stepped: the constraint and contact passes run, the avatar moves under a test
            // motion, and the bones stay rigid. Publishing Inst ourselves is what connects
            // the registered chains to the solve. Restored on stop.
            if (!ReferenceEquals(PhysBoneManager.Inst, _manager))
            {
                _prevInst = PhysBoneManager.Inst;
                PhysBoneManager.Inst = _manager;
                _tookOverInst = true;
            }

            _prevDisableTiming = PhysBoneManager.DisableTiming;
            _prevDebugTimeElapsed = PhysBoneManager.DebugTimeElapsed;

            // Hand the solver a fixed step instead of letting it accumulate Time.deltaTime,
            // which in edit mode is not a meaningful per-step quantity.
            PhysBoneManager.DisableTiming = true;
            PhysBoneManager.DebugTimeElapsed = BaseStep;

            // Distance culling measures from this point. The scene camera is what the user is
            // looking through, so cull from there; left at the origin, a bone far from world
            // zero could be culled and simply never move.
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null && view.camera != null)
                _manager.distanceCullOrigin = view.camera.transform.position;

            return true;
        }

        private static void Register()
        {
            foreach (VRCPhysBoneCollider col in _colliders)
            {
                if (col == null) continue;
                col.InitShape();
                _manager.AddCollider(col);
            }

            foreach (VRCPhysBone pb in _bones)
            {
                if (pb == null) continue;

                // AddPhysBone early-outs on ChainId.Null, so a component that never ran Init()
                // would be silently ignored. Mint an arbitrary-type id, which is what the SDK
                // itself uses for non-networked chains.
                if (pb.chainId == ChainId.Null)
                    AssignChainId(pb);

                // Populates pb.bones from the transform hierarchy. AddChains() calls this too,
                // but only reaches it after the chainId check above has passed.
                pb.InitTransforms(true);
                _manager.AddPhysBone(pb);
            }
        }

        // chainId is a public field but ChainId's constructor takes two raw ulongs and the
        // SDK's own id factories (BuildChainId / GenerateArbitraryChainId) are internal. Rather
        // than invent an id format, call the SDK's generator by reflection and fall back to a
        // GUID-derived pair - which is exactly what VRCPhysBoneBase.Init() does - if a future
        // SDK renames it.
        private static void AssignChainId(VRCPhysBone pb)
        {
            try
            {
                MethodInfo generate = typeof(VRCPhysBoneBase).GetMethod(
                    "GenerateArbitraryChainId",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                if (generate != null)
                {
                    object id = generate.Invoke(null, new object[] { ChainId.Type.Arbitrary });
                    if (id is ChainId chainId)
                    {
                        pb.chainId = chainId;
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // Fall through to the GUID path.
            }

            byte[] guid = Guid.NewGuid().ToByteArray();
            pb.chainId = new ChainId(BitConverter.ToUInt64(guid, 0), BitConverter.ToUInt64(guid, 8));
        }

        private static void DestroyManager()
        {
            if (_manager != null)
            {
                // Unregister before disposing, so the solver isn't left holding buffers that
                // reference components it will never be asked about again.
                foreach (VRCPhysBone pb in _bones)
                {
                    if (pb != null)
                        _manager.RemovePhysBone(pb);
                }
                foreach (VRCPhysBoneCollider col in _colliders)
                {
                    if (col != null)
                        _manager.RemoveCollider(col);
                }

                _manager.CompleteJob();

                // Only dispose a manager we created. A pre-existing one may belong to a play
                // session or another tool, and disposing it would break them.
                if (_host != null)
                    _manager.Dispose();
            }

            PhysBoneManager.DisableTiming = _prevDisableTiming;
            PhysBoneManager.DebugTimeElapsed = _prevDebugTimeElapsed;

            // Put Inst back exactly as it was. Leaving ours published would point the SDK's
            // own scheduler at a manager we are about to destroy.
            if (_tookOverInst)
            {
                if (ReferenceEquals(PhysBoneManager.Inst, _manager))
                    PhysBoneManager.Inst = _prevInst;
                _tookOverInst = false;
                _prevInst = null;
            }

            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }
            _manager = null;
        }

        // --- stepping ---------------------------------------------------------------------

        private static void OnEditorUpdate()
        {
            if (!_running)
            {
                EditorApplication.update -= OnEditorUpdate;
                return;
            }

            // Anything still alive? A previewed bone being deleted mid-preview would otherwise
            // leave the solver reading a destroyed component.
            for (int i = 0; i < _bones.Count; i++)
            {
                if (_bones[i] == null)
                {
                    Stop();
                    return;
                }
            }

            double now = EditorApplication.timeSinceStartup;
            float elapsed = (float)(now - _lastEditorTime);
            _lastEditorTime = now;

            if (_paused)
                return;

            // The editor's update rate is not fixed and can stall for seconds at a time (an
            // asset import, a compile). Clamp, or the preview catches up with a burst of steps
            // that looks like the bone was flung.
            elapsed = Mathf.Min(elapsed, 0.1f);

            float step = 1f / Mathf.Max(RefreshRate, 1);
            _accumulator += elapsed;

            int guard = 0;
            while (_accumulator >= step && guard++ < 8)
            {
                _accumulator -= step;
                Step(step);
            }

            SceneView.RepaintAll();
        }

        private static void Step(float deltaTime)
        {
            if (_manager == null)
                return;

            try
            {
                // Clip first, then the test motion: a clip poses the rig and the motion moves the
                // root it is posed on, so applying them the other way round would have the
                // motion overwritten by the clip's root curves.
                PhysBoneClipPlayback.Apply(deltaTime);

                // When a clip is driving the avatar, the test motion must not also write the
                // same root transform in the same step. Both writers target localPosition /
                // localRotation of the avatar root; whichever runs second would clobber the
                // other's curves and the solver would see a root that jitters between the two
                // poses each frame - the "stuck in place" report. Clip wins when it is
                // active; the test motion is still armed and resumes as soon as the clip
                // stops. The same guard lives in PhysBoneClipPlayback.HasClip / IsActive so
                // the motion panel can reflect it if it wants to.
                bool clipDriving = PhysBoneClipPlayback.IsActive;
                if (!clipDriving)
                    PhysBoneTestMotion.Apply(deltaTime);

                // Live tuning: every step, copy whatever the inspector (ours, VRChat's, the
                // Animation window, an Undo) wrote into the component back into the solver's
                // Bone/Chain copies. UpdateChains is gated on configHasUpdated, so without
                // this an edit to pull, spring, radius, a limit angle or any distribution
                // curve would sit on the component and never reach the solve. Pushing the
                // flag is what the SDK itself does on any change; doing it unconditionally
                // here is cheaper than detecting which field changed and cannot miss an edit.
                // It also carries isAnimated, which is the other half of the frozen-bones
                // bug: when a clip starts moving the rig but the chain copy still has
                // isAnimated == false, SolveAnimation never runs and SolveChain treats the
                // animation as drift to correct.
                PhysBoneLiveTuning.Push(_bones, _colliders);

                PhysBoneManager.DebugTimeElapsed = deltaTime;

                SceneView view = SceneView.lastActiveSceneView;
                if (view != null && view.camera != null)
                    _manager.distanceCullOrigin = view.camera.transform.position;

                // Schedules the constraint + PhysBone + contact passes and blocks until they
                // finish. finalizeImmediately is what makes this usable outside the player
                // loop: without it the jobs are scheduled and nothing ever completes them.
                VRCDynamicsScheduler.PreScheduleDynamics(true);

                SimulatedSeconds += deltaTime;
                StepCount++;

                // After the solve, so the sampled tip is the solved position rather than the
                // one from the previous step.
                PhysBonePreviewTrail.Sample(_bones);
            }
            catch (Exception e)
            {
                // A solver exception mid-step would otherwise repeat every editor update and
                // leave the bones displaced. Stop cleanly and say why once.
                Debug.LogError($"[PhysBone Handles] Preview stopped: the PhysBone solver threw. {e.GetType().Name}: {e.Message}");
                Stop();
            }
        }

        // --- teardown triggers ------------------------------------------------------------

        [InitializeOnLoadMethod]
        private static void HookEditorLifecycle()
        {
            // Every one of these can happen mid-preview, and all of them would otherwise leave
            // the avatar in a swung pose with no running preview to stop.
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += _ => Stop();
            EditorApplication.quitting += Stop;
            UnityEditor.SceneManagement.EditorSceneManager.sceneClosing += (_, __) => Stop();
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpening += (_, __) => Stop();
        }
    }
}
#endif
