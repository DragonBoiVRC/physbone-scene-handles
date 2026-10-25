# PhysBone Scene Handles

Free, open-source batch scene-view handles for editing VRChat `VRCPhysBoneCollider` and
`VRCPhysBone` components across multiple selected objects at once — drag one handle, apply
the change to a whole selection (Alt = just this one, Shift = equalize). No license key, no
phone-home license/HWID check, no OS check. Cross-platform.

The actual package lives in [`Packages/com.opensource.physbonehandles`](Packages/com.opensource.physbonehandles) —
see its README for what it does and how to use it, or
[`FEATURES.md`](Packages/com.opensource.physbonehandles/FEATURES.md) for the full feature
list.

## Status

**0.8.0** adds a **+** beside the PhysBone Parameter field that adds the `_IsGrabbed`, `_IsPosed`, `_Angle`, `_Stretch` and `_Squish` parameters to the avatar's FX controller. Compile-checked only; not click-tested.

**0.7.4** fixes bones sticking on the *second* preview. Stop used to queue chain removal and dispose
the manager without running a solve, so each component kept pointing into the dead manager (its
root definition, group number and shape). The next Start registered chains that were never solved.
Stop now runs one zero-length solve to drain the removals, and group numbers are reset on Stop and
Start. Compile-verified only.

**0.7.3** fixes the stuck-bone bug properly, and withdraws the part of 0.7.2 that caused it.

0.7.2 forced `isAnimated` true on every previewed component while a clip played. That was wrong
twice over. `isAnimated` is a **serialized user setting** — the "Is Animated" checkbox on the
PhysBone inspector — so the preview was permanently editing avatars; the rest-pose snapshot
restores Transforms, not component fields, so the tick survived Stop. And it caused the freeze it
was meant to fix: with the flag set, `SolveAnimation` re-bases the chain's rest pose onto the
bone's current rotation every frame, and on a chain nothing is keyframing, the only thing moving
those locals is the solver's own output from the previous step. The spring's target followed the
bone, the restoring force went to zero, and the chain held its bend. The tolerance is 1e-4 per
quaternion component, which any visible swing clears — hence "occasionally", meaning as soon as
the avatar moved enough to matter.

> **If you ran 0.7.2:** check **Is Animated** on the PhysBones you previewed with a clip and
> untick it if you did not set it yourself. `Tools > PhysBone Handles > Log Preview Diagnostics`
> lists the flag per chain.

In its place:

* **`isAnimated` is never written.** A chain whose bones really are keyframed does need that
  checkbox, so the preview detects when the active clip animates bones a previewed chain also
  simulates and names the components — the clip and the solver genuinely fight over those, and
  the fix is the user's setting, not ours to tick.
* **Reset actually resets.** The solver does not read bone rotations back at the start of a
  solve; for any simulated bone it reinstates its own previous value first, so a snapshot
  restore was overwritten within one step. Reset now re-registers the chains, which is also the
  button to reach for if one ever does wedge.
* **The preview no longer hijacks the Animation window.** `AnimationMode` is one global editor
  state; the preview now only stops a session it started.

Live tuning from 0.7.2 is unchanged: drag Pull, Spring, radius, a limit angle or any
distribution curve and the running solve updates, via the SDK's own `ApplyConfigurationChanges`.

**0.7.1** fixes the live preview not simulating: the avatar moved under a test motion but the
bones stayed rigid. The solver is reached through the static `PhysBoneManager.Inst`, which is
only ever assigned in `Awake` — and `PhysBoneManager` has no `[ExecuteAlways]`, so `Awake` does
not run in edit mode. The preview registered every chain with a manager the solver was then
never asked to step. It now publishes its manager as `Inst` while running and restores the
previous value on stop.

**0.7.0** adds a live preview: VRChat's own PhysBone solver, run on the selected components in
edit mode, with pause/frame-step, 72–144 Hz, test motions, AnimationClip playback, grab/pose
testing, a throwaway test collider, tip trails, a baseline freeze, a live `_Angle`/`_Stretch`/
`_Squish` readout, an inspector 3D viewport, and per-bone curve sampling markers. Starting the
preview snapshots every Transform the solver can reach and stopping restores it, so nothing on
your avatar is changed.

That whole subsystem is **compile-verified only** — the panel layout, the behaviour in motion,
the grab/pose interaction and the inspector viewport have not been click-tested in Unity. The
batch handles and the Auto Collider Generator have been used in a real project.

## Installing

**Via VCC / ALCOM (VPM):** Add this repository's listing URL —
`https://dragonboivrc.github.io/physbone-scene-handles/index.json` — under Settings > Packages >
Add Repository, then add "PhysBone Scene Handles" to your project from the package list.

**Manually:** Download the `.zip` or `.unitypackage` from the
[latest release](https://github.com/DragonBoiVRC/physbone-scene-handles/releases/latest) and
import/extract it into your project.

## Contributing / publishing new versions

This repo is built from VRChat's official
[template-package](https://github.com/vrchat-community/template-package) automation. To ship
a new version: bump `version` in `Packages/com.opensource.physbonehandles/package.json`,
push to `main`, then manually run the **Build Release** action from the Actions tab. That
creates a GitHub Release (zip + `.unitypackage`) tagged with the new version, which in turn
triggers **Build Repo Listing** to regenerate the VPM listing published on GitHub Pages.

See VRChat's [Create a Package Listing](https://vcc.docs.vrchat.com/guides/create-listing/)
guide for the full picture of how this automation works.

## License

MIT — see [`Packages/com.opensource.physbonehandles/LICENSE`](Packages/com.opensource.physbonehandles/LICENSE).
