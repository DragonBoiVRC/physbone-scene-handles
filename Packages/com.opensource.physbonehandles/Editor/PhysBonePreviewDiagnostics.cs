#if PBHANDLES_VRCSDK_PRESENT
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Tools > PhysBone Handles > Log Preview Diagnostics.
    //
    // A preview that misbehaves is hard to report on, because the interesting state lives inside
    // the solver and the only visible symptom is "the bones look wrong". This dumps the handful of
    // values that distinguish the ways a chain can sit still, so a bug report can say which one it
    // is instead of describing the appearance:
    //
    //   Is Animated     - the setting that makes the solver re-base the chain's rest pose onto the
    //                     bones' current rotation every frame. On a chain nothing is keyframing,
    //                     that is indistinguishable from the spring being dead: the target follows
    //                     the bone and the chain holds its bend. First thing to check on a chain
    //                     that will not move.
    //   Simulated bones - a chain whose bones all end up non-simulated cannot move at all. Happens
    //                     when a leaf bone has no child and no Endpoint Position.
    //   Angle/Stretch   - the solver's own per-chain output. Non-zero and changing means the solve
    //                     is running and reaching this chain, which separates "the solver is not
    //                     stepping" from "the solve runs but the chain is wedged".
    //   Clip conflict   - the active clip keyframes bones this chain also simulates. See
    //                     PhysBoneClipOverlap.
    //
    // Everything here is read off the components and the public SDK API, so it works without a
    // reference to Unity.Mathematics (which reading PhysBoneManager's Chain/Bone structs would
    // need, since their fields are float3/quaternion).
    internal static class PhysBonePreviewDiagnostics
    {
        private const string MenuPath = "Tools/PhysBone Handles/Log Preview Diagnostics";

        [MenuItem(MenuPath, false, 102)]
        private static void Log()
        {
            Debug.Log(Build());
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate()
        {
            return PhysBonePreview.IsRunning;
        }

        internal static string Build()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[PhysBone Handles] Preview diagnostics\n");

            if (!PhysBonePreview.IsRunning)
            {
                sb.Append("  Preview is not running.");
                return sb.ToString();
            }

            sb.Append("  Steps: ").Append(PhysBonePreview.StepCount)
              .Append("   Simulated: ").Append(PhysBonePreview.SimulatedSeconds.ToString("0.00")).Append("s")
              .Append("   Rate: ").Append(PhysBonePreview.RefreshRate).Append(" Hz")
              .Append(PhysBonePreview.IsPaused ? "   (paused)" : string.Empty).Append('\n');

            sb.Append("  Motion: ").Append(PhysBoneTestMotion.Describe(PhysBoneTestMotion.Current));
            if (PhysBoneClipPlayback.HasClip)
            {
                sb.Append("   Clip: ").Append(PhysBoneClipPlayback.Clip.name)
                  .Append(PhysBoneClipPlayback.IsActive ? " (driving - test motion is held off)" : " (inactive)");
            }
            sb.Append('\n');

            foreach (VRCPhysBone pb in PhysBonePreview.PreviewedBones)
            {
                if (pb == null)
                {
                    sb.Append("  - (deleted component)\n");
                    continue;
                }

                PhysBonePreviewReadback.Params p = PhysBonePreviewReadback.Read(pb);
                sb.Append("  - ").Append(pb.name)
                  .Append("  bones:").Append(pb.bones != null ? pb.bones.Count : 0)
                  .Append("  simulated:").Append(CountSimulated(pb))
                  .Append("  isAnimated:").Append(pb.isAnimated ? "ON" : "off")
                  .Append("  angle:").Append(p.angle.ToString("0.000"))
                  .Append("  stretch:").Append(p.stretch.ToString("0.000"))
                  .Append("  squish:").Append(p.squish.ToString("0.000"));

                if (p.isGrabbed) sb.Append("  GRABBED");
                if (p.isPosed) sb.Append("  POSED");
                if (PhysBoneClipOverlap.Conflicting.Contains(pb))
                    sb.Append("  CLIP CONFLICT");
                if (pb.isAnimated)
                    sb.Append("\n      'Is Animated' is on, so the solver re-bases this chain's rest pose every frame. " +
                        "Correct if these bones are keyframed; if they are not, it is why the chain holds its bend.");

                sb.Append('\n');
            }

            return sb.ToString();
        }

        // Mirrors the solver's own rule for whether a bone moves at all (PhysBoneManager.AddChains
        // setting Bone.simulatedType): a bone swings its child, or a vector standing in for one,
        // and a bone with neither is registered but inert. That is the quiet way a chain ends up
        // shorter than it looks - a leaf with no Endpoint Position contributes nothing.
        private static int CountSimulated(VRCPhysBone pb)
        {
            if (pb.bones == null)
                return 0;

            bool hasEndpoint = pb.endpointPosition != Vector3.zero;
            int count = 0;

            for (int i = 0; i < pb.bones.Count; i++)
            {
                VRCPhysBoneBase.Bone b = pb.bones[i];

                if (b.childCount == 1)
                    count++;
                else if (b.childCount > 1)
                {
                    if (pb.multiChildType == VRCPhysBoneBase.MultiChildType.First)
                        count++;
                    else if (pb.multiChildType == VRCPhysBoneBase.MultiChildType.Average
                             && b.averageChildPos != Vector3.zero)
                        count++;
                }
                else if (hasEndpoint)
                    count++;
            }

            return count;
        }
    }
}
#endif
