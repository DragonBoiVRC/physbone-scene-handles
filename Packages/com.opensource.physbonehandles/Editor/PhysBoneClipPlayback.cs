#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Plays one of the avatar's own AnimationClips while the preview runs, so a chain can be
    // tested against the motion it will actually see rather than a generic sway.
    //
    // This is the difference between "the hair looks fine when it sways" and "the hair clears the
    // shoulder during the dance emote you are shipping". The built-in test motions are useful for
    // tuning Pull and Spring in the abstract; a real clip is what tells you whether a specific
    // emote has a problem.
    //
    // AnimationMode is the editor's own clip-scrubbing facility - the same machinery the
    // Animation window uses to pose a rig without entering play mode. Two things make it the
    // right tool here rather than a hazard:
    //
    //   * AnimationMode.SampleAnimationClip writes the clip's pose onto the hierarchy for a given
    //     time, which is exactly what the solver needs to see each step.
    //   * AnimationMode records what it touches and restores it on StopAnimationMode, so the
    //     animated bones are returned by Unity itself. The preview's own rest-pose snapshot still
    //     covers everything as a second layer, because the clip and the solver can touch
    //     overlapping transforms and only the snapshot knows the pre-preview state of both.
    internal static class PhysBoneClipPlayback
    {
        private static AnimationClip _clip;
        private static Animator _animator;
        private static GameObject _root;
        private static float _time;
        private static bool _active;

        // AnimationMode is a single global editor state, shared with the Animation window. If that
        // window already has a preview open when we start, StartAnimationMode would nest and our
        // StopAnimationMode would tear down *their* session, reverting whatever they were editing.
        // So ownership is tracked: we only stop what we started.
        private static bool _ownsAnimationMode;

        internal static bool HasClip => _clip != null;
        internal static bool IsActive => _active;

        internal static AnimationClip Clip
        {
            get => _clip;
            set
            {
                if (_clip == value)
                    return;
                Stop();
                _clip = value;
                _time = 0f;
            }
        }

        internal static float Time => _time;
        internal static float Length => _clip != null ? _clip.length : 0f;

        // Start sampling against `root`. Needs the avatar root, not the bone - a clip's curves
        // are addressed by path relative to the object the Animator sits on.
        internal static void Begin(Transform root, IReadOnlyList<VRCPhysBone> previewedBones)
        {
            if (_clip == null || root == null || _active)
                return;

            _root = root.gameObject;
            _animator = _root.GetComponentInParent<Animator>();
            if (_animator == null)
                _animator = _root.GetComponent<Animator>();

            // No Animator means the clip's paths have nothing to resolve against. Sampling would
            // silently do nothing, which is worse than saying so.
            if (_animator == null)
            {
                Debug.LogWarning($"[PhysBone Handles] '{root.name}' has no Animator, so an AnimationClip " +
                    "cannot be sampled onto it. Use one of the built-in test motions instead.");
                _root = null;
                return;
            }

            if (AnimationMode.InAnimationMode())
            {
                // Someone else is already previewing - almost always the Animation window. Share
                // the session rather than restarting it, and say so: their clip's properties are
                // in the same modification set as ours and will be re-asserted every time we
                // sample, which can look like the preview ignoring part of the pose.
                _ownsAnimationMode = false;
                Debug.LogWarning("[PhysBone Handles] The editor is already in animation preview mode " +
                    "(usually the Animation window). The clip will still play, but the two previews " +
                    "share one sampling session and can override each other. Close the Animation " +
                    "window's preview for a clean read.");
            }
            else
            {
                AnimationMode.StartAnimationMode();
                _ownsAnimationMode = true;
            }

            _active = true;
            _time = 0f;

            PhysBoneClipOverlap.Evaluate(_clip, _animator.transform, previewedBones);
        }

        internal static void Stop()
        {
            if (!_active)
                return;

            _active = false;
            _root = null;
            _animator = null;
            PhysBoneClipOverlap.Clear();

            // Returns everything the clip wrote to its pre-sampling values. Only if the session is
            // ours: stopping someone else's would revert their edits instead.
            if (_ownsAnimationMode && AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();

            _ownsAnimationMode = false;
        }

        internal static void Reset()
        {
            _time = 0f;
        }

        // Advance and sample. Called once per solver step, before the solve, so the bones react
        // to the clip's new pose in the same step rather than one behind it.
        internal static void Apply(float deltaTime)
        {
            if (!_active || _clip == null || _root == null)
                return;

            _time += deltaTime;
            if (_clip.length > 0f)
                _time = Mathf.Repeat(_time, _clip.length);

            AnimationMode.BeginSampling();
            try
            {
                AnimationMode.SampleAnimationClip(_root, _clip, _time);
            }
            finally
            {
                // In a finally block because an exception mid-sampling would otherwise leave the
                // editor stuck inside a sampling block, which breaks the Animation window too.
                AnimationMode.EndSampling();
            }
        }
    }
}
#endif
