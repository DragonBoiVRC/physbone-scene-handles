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

**0.7.2** fixes a second way the preview could look frozen: when an AnimationClip moved the rig
the cached `Chain.isAnimated` stayed false and `configHasUpdated` stayed false, so
`SolveAnimation` never ran and `UpdateChains` never refreshed the `Bone`/`Chain` copies — bones
held in world space while the root moved under them. The preview now pushes `isAnimated` and
`configHasUpdated` each step (and stops the clip and the test motion fighting over the same
root), and inspector edits take effect live via `ApplyConfigurationChanges` instead of waiting
for a stop/start cycle. Drag Pull, Spring, radius, a limit angle or any distribution curve and
the running solve updates.

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
