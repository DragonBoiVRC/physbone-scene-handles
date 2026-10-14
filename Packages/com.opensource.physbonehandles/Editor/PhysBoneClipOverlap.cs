#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Answers one question before a clip-driven preview starts: does this clip keyframe any bone
    // that a previewed PhysBone also simulates?
    //
    // Why it matters. A clip and the solver both write the same localRotation, and they disagree:
    //
    //   * The solver restores its own previous value at the top of each solve
    //     (`transformData.localRotation = transformData.prevLocalRotation` for any simulated
    //     bone), so it overwrites whatever the clip just sampled.
    //   * AnimationMode re-asserts the clip's value at the start of the next sample.
    //
    // Neither side yields, and the visible result is a chain that jitters or sits rigid - the
    // "stuck bone" report. The SDK has a setting for exactly this case: `isAnimated` ("Is
    // Animated" on the inspector) makes the solver re-read the animated pose each frame and treat
    // it as the chain's new rest pose, which is the right behaviour when the bones really are
    // keyframed. It is also wrong, and actively destructive, on a chain that is not - see
    // PhysBoneLiveTuning for what happened when the preview set it unconditionally.
    //
    // So the preview does not decide. It reports the overlap and names the components, because
    // "Is Animated" changes how the avatar behaves in game and is the user's call, not ours.
    internal static class PhysBoneClipOverlap
    {
        // Components the clip and the solver would fight over, and that do not have isAnimated set.
        internal static readonly List<VRCPhysBone> Conflicting = new List<VRCPhysBone>();

        internal static bool HasConflict => Conflicting.Count > 0;

        internal static void Clear()
        {
            Conflicting.Clear();
        }

        // Called when clip playback begins. `animatorRoot` is the transform the clip's curve paths
        // are relative to - the same object AnimationMode samples onto.
        internal static void Evaluate(AnimationClip clip, Transform animatorRoot, IReadOnlyList<VRCPhysBone> bones)
        {
            Conflicting.Clear();
            if (clip == null || animatorRoot == null || bones == null || bones.Count == 0)
                return;

            HashSet<Transform> animated = CollectAnimatedTransforms(clip, animatorRoot);
            if (animated.Count == 0)
                return;

            for (int i = 0; i < bones.Count; i++)
            {
                VRCPhysBone pb = bones[i];
                if (pb == null || pb.bones == null || pb.isAnimated)
                    continue;

                for (int b = 0; b < pb.bones.Count; b++)
                {
                    Transform t = pb.bones[b].transform;
                    if (t != null && animated.Contains(t))
                    {
                        Conflicting.Add(pb);
                        break;
                    }
                }
            }

            if (Conflicting.Count > 0)
                Debug.LogWarning(Describe(clip));
        }

        internal static string Describe(AnimationClip clip)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[PhysBone Handles] '").Append(clip != null ? clip.name : "clip")
              .Append("' keyframes bones that these PhysBones also simulate, so the clip and the solver will fight over them and the chain can look stuck:");
            for (int i = 0; i < Conflicting.Count; i++)
            {
                VRCPhysBone pb = Conflicting[i];
                sb.Append("\n  - ").Append(pb != null ? pb.name : "(deleted)");
            }
            sb.Append("\nTick 'Is Animated' on those components if their bones are meant to be keyframed - ")
              .Append("the solver will then use the animated pose as each frame's rest pose. ")
              .Append("Leave it unticked and pick a clip that does not touch the chain instead.");
            return sb.ToString();
        }

        // Transform curves address their target by path relative to the Animator's object; muscle
        // curves address nothing directly and are retargeted onto the humanoid rig at runtime, so
        // the humanoid bones have to be gathered from the Avatar rather than from the clip.
        private static HashSet<Transform> CollectAnimatedTransforms(AnimationClip clip, Transform animatorRoot)
        {
            HashSet<Transform> result = new HashSet<Transform>();

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform))
                    continue;

                Transform t = string.IsNullOrEmpty(binding.path)
                    ? animatorRoot
                    : animatorRoot.Find(binding.path);
                if (t != null)
                    result.Add(t);
            }

            if (clip.isHumanMotion)
            {
                Animator animator = animatorRoot.GetComponentInParent<Animator>();
                if (animator != null && animator.isHuman)
                {
                    foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
                    {
                        // LastBone is a count, not a bone, and asking for it throws.
                        if (bone == HumanBodyBones.LastBone)
                            continue;

                        Transform t = animator.GetBoneTransform(bone);
                        if (t != null)
                            result.Add(t);
                    }
                }
            }

            return result;
        }
    }
}
#endif
