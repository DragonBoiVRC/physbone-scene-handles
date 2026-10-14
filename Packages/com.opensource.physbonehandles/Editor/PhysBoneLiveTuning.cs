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
    internal static class PhysBoneLiveTuning
    {
        // Called once per solver step, before the solve.
        internal static void Push(IReadOnlyList<VRCPhysBone> bones, IReadOnlyList<VRCPhysBoneCollider> colliders)
        {
            if (bones != null)
            {
                bool clipDriving = PhysBoneClipPlayback.IsActive;
                for (int i = 0; i < bones.Count; i++)
                {
                    VRCPhysBone pb = bones[i];
                    if (pb == null)
                        continue;

                    // When a clip is driving the rig the chain must run SolveAnimation,
                    // which is gated on chain.isAnimated. That flag is copied from the
                    // component's isAnimated in UpdateChains. Many chains leave it false
                    // because they are not normally animated; forcing it while the clip
                    // is active is what makes the bones follow the clip instead of
                    // holding in world space. The component value is not restored - the
                    // preview is edit-mode only and the solver's copy is what matters, and
                    // rest-pose restore puts the transforms back regardless.
                    if (clipDriving && !pb.isAnimated)
                        pb.isAnimated = true;

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
