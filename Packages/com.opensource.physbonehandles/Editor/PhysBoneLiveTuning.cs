#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Makes an inspector edit take effect in the running preview, so a slider can be dragged and
    // the bones change under it - no stop, adjust, restart cycle.
    //
    // Why this is needed at all:
    //
    //   The solver does not read the component every step. AddChains copies pull, spring, radius,
    //   the limit angles and every distribution-curve result into its own Bone/Chain structs once,
    //   at registration. After that, the only thing that refreshes those copies is UpdateChains,
    //   and UpdateChains skips a chain whose `configHasUpdated` is false:
    //
    //       if (!vRCPhysBoneBase.configHasUpdated) continue;
    //
    //   In game that flag is raised by the component's own property setters and by animation. In
    //   edit mode nothing raises it, so the preview kept simulating the values that were in place
    //   when it started, and changing a slider appeared to do nothing.
    //
    // Why raise the flag unconditionally rather than detect changes:
    //
    //   Detecting "did the user touch a field" means either polling ~40 serialized properties per
    //   component per step, or hooking every inspector that can write one (ours, VRChat's, the
    //   Animation window, a third-party tool, an Undo). Raising the flag is what the SDK itself
    //   does on any change, and the work behind it is one pass over the chain's bones recomputing
    //   values the solver was going to read anyway. For the handful of chains a preview covers
    //   that is cheaper than the detection would be, and it cannot miss an edit.
    //
    //   It is also the same path the game takes when a slider is animated, which is the point:
    //   the preview is not approximating live tuning, it is using the mechanism built for it.
    //
    // What it deliberately does not touch: originalLocalPosition / originalLocalRotation. Those
    // hold the rest pose inside the solver, and UpdateChains leaves them alone - so re-applying
    // the config every step does not accumulate drift or move the chain's origin.
    //
    // And what it must never touch: `isAnimated`. 0.7.2 forced that field true on every previewed
    // component while a clip was playing, and it was wrong twice over.
    //
    //   * `isAnimated` is a serialized user setting - the "Is Animated" checkbox on the PhysBone
    //     inspector, tooltipped "Allows bone transforms to be animated. Each frame bone rest
    //     position will be updated according to what was animated." Writing it from a preview
    //     edited the avatar permanently: the rest-pose snapshot restores Transforms, not
    //     component fields, so the tick survived Stop and changed in-game behaviour.
    //
    //   * It also caused the exact freeze it was meant to fix. With the flag set, the solver runs
    //     SolveAnimation, which re-bases the chain's rest pose onto whatever the bone's local
    //     rotation currently is:
    //
    //         if (HasChanged(transformData.prevLocalRotation, transformData.localRotation))
    //             bone.originalLocalRotation = transformData.localRotation;
    //
    //     On a chain the clip does not animate - every hair or tail chain - the only thing that
    //     changes those locals is the solver's own output from the previous step. So the spring's
    //     target followed the bone, the restoring force went to zero, and the chain held whatever
    //     bend it was in. The tolerance is 1e-4 per quaternion component, which any visible swing
    //     clears, which is why it showed up "occasionally" - precisely once the avatar started
    //     moving enough to matter.
    //
    // A chain whose bones really are keyframed does need that checkbox, and the preview now says
    // so (PhysBoneClipOverlap) instead of ticking it for you.
    internal static class PhysBoneLiveTuning
    {
        // Called once per solver step, before the solve.
        internal static void Push(IReadOnlyList<VRCPhysBone> bones, IReadOnlyList<VRCPhysBoneCollider> colliders)
        {
            if (bones != null)
            {
                for (int i = 0; i < bones.Count; i++)
                {
                    VRCPhysBone pb = bones[i];
                    if (pb == null)
                        continue;

                    pb.ApplyConfigurationChanges();
                }
            }

            if (colliders != null)
            {
                for (int i = 0; i < colliders.Count; i++)
                {
                    VRCPhysBoneCollider col = colliders[i];
                    if (col == null)
                        continue;

                    // Queues the collider for UpdateShape() in the solver's own
                    // UpdateColliderShapes pass, which runs in the same step. This is what makes
                    // a radius or height edit on a collider move the bones immediately.
                    col.ApplyConfigurationChanges();
                }
            }
        }
    }
}
#endif
