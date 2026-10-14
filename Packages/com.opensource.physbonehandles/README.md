# PhysBone Scene Handles

A free, open-source, cross-platform batch scene-view editor for VRChat `VRCPhysBoneCollider`,
`VRCPhysBone`, `VRCContactSender`, and `VRCContactReceiver` components, plus a tool to
auto-generate a full set of body colliders from your avatar's mesh. No license key, no
phone-home license/HWID check, no OS check — it's a normal Unity Editor package and should
work anywhere Unity + the VRChat SDK does, Linux included.

For a plain checklist of everything it does, see [`FEATURES.md`](FEATURES.md).

## Provenance

This is an **original implementation**, written from scratch against VRChat's own public
`VRC.Dynamics`/`VRC.SDK3.Dynamics.PhysBone` SDK (decompiled from VRChat's official, publicly
distributed SDK DLLs purely to confirm exact field names — that's normal SDK usage, not
reverse-engineering a third party's product) and the publicly-described feature set /
promotional GIF of Dreadrith's "Avatar Dynamics Overhaul" (batch, additive scene-handle
editing of PhysBoneColliders with Alt = "edit only this one" / Shift = "equalize across
selection" modifiers). No code, IL, or implementation detail from that tool's binary was
read, referenced, or ported — the interaction *idea* (batch handles with modifier keys) is
functionality, not copyrightable expression, and every line here is new. The Auto Collider
Generator (mesh-weight-based capsule fitting) has no equivalent in that tool at all — it's
new territory built for this package.

## What it does

### Batch collider/PhysBone scene handles

- Select one or more objects that have a `VRCPhysBoneCollider` component. Handles for
  Radius, Height (capsule only), Position, and Rotation (capsule only) are drawn on **every**
  selected collider at once in the Scene view.
- Select one or more objects that have a `VRCPhysBone` component instead, and the same
  "Radius" toggle draws one grab handle per bone in the chain, sticking out sideways from
  each node. Dragging a handle shapes the `radiusCurve` (the per-position multiplier on the
  base `radius`) at that point — e.g. grab the middle of a tail and pull it out to bulge it,
  or pull the tip in to taper it, without hand-editing curve keys in the inspector.
  Branching chains are fully walked: a PhysBone on a Hand draws all five fingers, not just
  whichever one happens to be the first child. A bone that several strands pass through gets
  one handle, not a stack of them fighting over the same curve key.
- With "Position" on, a `VRCPhysBone`'s **Endpoint Position** gets a drag handle at the chain
  tip, so you can see and place that virtual extra segment instead of guessing at three raw
  numbers in the inspector.
- A small toggle panel in the bottom-right of the Scene view turns each handle type on/off
  (green = active, red = off).
- While Position or Rotation editing is active, Unity's own built-in Move/Rotate gizmo for
  the selected object is temporarily hidden (`Tools.hidden`), since it otherwise sits right
  on top of our handles and it's easy to grab the wrong one and drag the actual bone.
- Drag any handle:
  - **No modifier** — the delta you drag is applied to every selected collider/chain (batch,
    additive).
  - **Alt** — only the one you're actually dragging changes.
  - **Shift** — every selected one is set to the exact same value as the one you dragged
    (equalize).
- `GameObject > PhysBone Handles` menu:
  - **Add VRC Phys Bone Collider to Selection** / **Add VRC Phys Bone to Selection** — adds
    the component to every selected object that doesn't already have one.
  - **Copy Collider Settings (Active -> Selection)** — copies shape/radius/height/bounds
    behavior (not root/position/rotation, which are per-bone) from the active object's
    collider onto the rest of the selection.
  - **Mirror Settings to Other Side** — finds each selected object's opposite-side twin by
    name (`Left`/`Right`, `_L`/`_R`, `.L`/`.R`, `-L`/`-R`, and lowercase variants) and copies
    its `VRCPhysBoneCollider` / `VRCContactSender` / `VRCContactReceiver` across, flipping
    `position` and `rotation` about X and repointing a self-referencing `rootTransform` at the
    target. The copy goes through `SerializedObject` property-by-property rather than naming
    fields, so every serialized setting comes over — including any the SDK adds later. Adds
    the component to the twin if it doesn't have one yet. Objects with no unambiguous side
    marker, or no twin in the scene, are skipped and listed in a console warning.

### Angle limit handles

A fifth **"Limits"** toggle in the bottom-right panel draws a `VRCPhysBone`'s angle limits
directly on the bones. Limits are otherwise the hardest part of a PhysBone to set up blind —
"Max Angle 45" in the inspector tells you nothing about where the bone can actually swing to,
so the usual loop is type a number, enter play mode, shake the avatar, come back, type another
number.

- **Angle** draws a cone from each joint, opening to Max Angle around the bone's rest
  direction. **Polar** draws an elliptical cone using both angles, for a joint that should
  swing freely one way and barely at all the other. **Hinge** draws a flat fan in the plane
  the bone is allowed to rotate in.
- Orientation comes from the component's own **Limit Rotation** field, not from the bone
  transform, so a rotated limit draws rotated.
- A grab handle on the first joint's rim edits Max Angle with a live degree readout, same
  Alt / Shift batch rules as every other handle here.
- The limit fields are read and written through `SerializedObject` by property name rather
  than direct field access. It costs a little speed, but if a future SDK renames or drops one
  of them this feature quietly switches itself off instead of breaking the whole package's
  compile.

### Contact scene handles

The same batch scene-view editing, extended to `VRCContactSender` and `VRCContactReceiver`.
Both share one implementation (they're both just a `ContactBase` under the hood), so
selecting a Sender and a Receiver together batches them exactly like selecting two of the
same type would.

- Handles for **Size**, Position, and Rotation are drawn on every selected Contact at once,
  same Alt (only this one) / Shift (equalize) / no-modifier (batch additive) drag behavior
  as the collider handles above.
- Contact's third shape, **Box** (which `VRCPhysBoneCollider` doesn't have), gets three pairs
  of opposing face-center sliders — one per axis — that grow the box symmetrically from its
  center, the same way the capsule height handles work. Sphere only shows a radius handle
  (rotation is meaningless on a sphere, so it's hidden); Capsule shows radius, height, and
  rotation.
- Its own toggle panel in the bottom-**left** of the Scene view (the collider/PhysBone panel
  above is bottom-right) so the two never overlap if you select a collider and a Contact at
  the same time.

### Collider picker

Select one or more `VRCPhysBone` objects and an **"Edit Colliders"** toggle appears in the
top-right of the Scene view. Turn it on and every `VRCPhysBoneCollider` on the same avatar
(scoped via `VRCAvatarDescriptor`) shows up as a small clickable sphere — green if it's
already in the active PhysBone's Colliders list, red if it isn't. Click a sphere to toggle
it, instead of dragging entries into the list one at a time (especially useful now that the
Auto Collider Generator can leave you with 16+ body colliders to choose from). With multiple
PhysBones selected, a click toggles that collider for all of them together; hold **Alt** to
affect only the active one.

### Auto Collider Generator

`Tools > PhysBone Scene Handles > Auto Collider Generator` — fits a `VRCPhysBoneCollider` to
each of an avatar's core Humanoid bones (Hips, Spine, Chest, Shoulders, Upper/Lower Arms,
Upper/Lower Legs, Neck, Head) automatically:

- Point it at your avatar's root (needs a Humanoid `Animator`). It lists every
  `SkinnedMeshRenderer` under the avatar with a checkbox — pick which mesh(es) count as "the
  body" (it guesses using a mesh named "Body", or the highest-vertex mesh otherwise).
  **This matters**: leaving clothing/hair/accessories checked will inflate colliders past the
  actual body, since a flared skirt weighted to Hips (for example) gets treated as part of
  the hips.
- **Radius** is fit from actual mesh geometry: for each bone, it looks at which vertices are
  dominantly skin-weighted to it, recenters on their true centroid (a bone's pivot is rarely
  dead-center of its own cross-section), and takes a percentile of the spread — controlled by
  the **Fit Tightness** slider (lower = tighter, ignores more of the wide/outlier parts).
- **Length** comes from the skeleton directly — the actual distance from the bone to its
  child joint (e.g. UpperArm → LowerArm) — not from vertex weighting, so capsules reliably
  span the full limb even on rigs with twist bones that would otherwise fragment the mesh
  weighting data for the middle of a limb.
- **Radius Margin** inflates every radius afterward, so colliders sit slightly outside the
  skin rather than exactly on it.
- A live cyan wireframe preview in the Scene view shows exactly what Apply would create —
  updates as you drag the sliders or toggle bones/meshes, no need to actually apply to see
  the effect.
- Hit **Scan**, review the per-bone list (uncheck anything you don't want), then **Apply**.
  "Skip bones that already have a Collider" (on by default) leaves anything you've since
  hand-tuned alone on future runs.
- It only knows about Unity's Humanoid rig mapping, so custom jiggle bones (breasts, etc.)
  aren't covered — add those with the batch-add menu command above instead, then size them
  with the scene handles.

### Set Root to Self

`VRCPhysBoneCollider`, `VRCPhysBone`, `VRCContactSender`, and `VRCContactReceiver` all have a
Root Transform field that's very often just "this same GameObject" — but there's no built-in
one-click way to say that, so you end up dragging the object onto its own field. This adds
that:

- A small **"S"** button appears directly inside the Root Transform row of each of those
  four components' inspectors, right next to VRC's own object field — click it to set Root
  to that GameObject's own Transform. Works with multiple objects selected at once (each
  gets set to its own Transform, not all to the same one).
- The same action is also available from the component's **⋮** context menu (or right-click
  its header) as **"Set Root to Self"**, regardless of whether the inline button is showing.

The inline button only exists because VRChat's SDK already ships its own inspector for all
four components, and Unity has no supported way to add a control to a field row inside
someone else's Editor. It works by Harmony-patching the internal Unity method every property
field in the entire Editor funnels through (`UnityEditor.PropertyHandler.OnGUI`), found by
disassembling Unity's own IL rather than guessing — not anything VRChat-specific. That's
about as deep into undocumented internals as this package goes, and it's meaningfully more
fragile than everything else here: a future Unity or VRChat SDK update could silently break
it. If that happens the lookup fails gracefully (a console warning, nothing crashes) and you
still have the context menu item, which doesn't depend on any of this.

### Custom component UI

The other half of the scene handles: `VRCPhysBone` and `VRCPhysBoneCollider` get a replacement
inspector in place of the one the SDK ships. The stock PhysBone inspector is a flat list of
~40 fields, so setting up a tail means scrolling past forces to find limits, scrolling back
for the radius, then switching to the Scene view to find out whether any of it worked.

- **Collapsible sections** — Transforms, Forces, Limits, Collisions, Stretch & Squish,
  Grab & Pose, Options, Gizmos. Expand state is stored in the SDK's *own* `foldout_*` fields,
  so a section you open here is open in the stock inspector too.
- **Arm a handle from the row it belongs to.** Rows that have a Scene-view handle in this
  package carry a small **✐** button at the end: Endpoint Position, Limit Type, and the
  collider's Radius / Height / Position / Rotation. Click it and that handle switches on, no
  trip to the corner toggle panel.
- **On/off settings ride on their section header** as a green/red pill, matching how the rest
  of this package signals state — Allow Collision on Collisions, Allow Grabbing on Grab &
  Pose, Show Gizmos on Gizmos, Inside/Outside Bounds on the collider's Shape row.
- **The distribution curves are hidden by default.** Every force (Pull, Spring, Stiffness,
  Gravity, Gravity Falloff, Immobile) has a paired `AnimationCurve` that most setups never
  touch. An **Advanced** pill on the Forces header brings them back, inline beside each value
  rather than on their own rows.
- **Fields that don't apply aren't shown.** Max Angle Z only appears for a Polar limit;
  Limit Type None says so instead of showing angle fields that do nothing; a Sphere collider
  hides Height and Rotation, exactly like the handles do.
- **Opt out:** `Tools > PhysBone Handles > Use Custom Component UI`. Off restores VRChat's own
  inspector exactly, and takes effect immediately without reselecting the component.

How it replaces the SDK's inspector, since `[CustomEditor]` alone isn't enough: the SDK ships
its own `VRCPhysBoneEditor` / `VRCPhysBoneColliderEditor`, precompiled, carrying the same
attribute for the same types. Unity honours exactly one editor per type and theirs won, which
is why 0.6.0 shipped this UI and nobody could see it. `CustomEditorOverridePatch` now hooks
the one place Unity decides (`UnityEditor.CustomEditorAttributes.FindCustomEditorType*`) and
swaps in ours — and stands down when the toggle is off, which is what makes "off" give you
the real SDK inspector rather than a bare field list. If a future Unity renames that internal
method, the hook misses, a warning is logged, and you get VRChat's inspector plus every Scene
handle: 0.5.0's behaviour, nothing broken.

Every field is looked up by serialized name and skipped if it's missing, so if a future SDK
renames one, that single row disappears and the rest of the component still draws. The names
come from VRChat's own serialized YAML output for the components rather than from any
decompilation.

### Live preview (edit-mode simulation)

Select a PhysBone and a **Live Preview** panel appears bottom-left in the Scene view
(`Tools > PhysBone Handles > Show Live Preview Panel`). It runs **VRChat's own PhysBone
solver** on the selected components in edit mode — no Play mode, no upload-and-test loop.

Start/stop, pause with single-iteration frame-stepping, 72/90/120/144 Hz (the same bone really
does behave differently at 72 and 144), test motions (sway, gust, circle, drop, walk) with
amount and speed, playback of one of the avatar's own AnimationClips, click-to-grab with
drag-to-pull and optional pose-on-release, a throwaway drag-through test collider, tip trails,
a ❄ baseline freeze for before/after comparison, a live `_Angle`/`_Stretch`/`_Squish` readout
with `_IsGrabbed`/`_IsPosed` flags in the inspector, a small orbitable 3D viewport in the
inspector, and per-bone curve sampling markers that show what a force's distribution curve
actually resolves to on each bone along each strand.

**Nothing on your avatar is changed.** Every Transform the solver can reach is snapshotted
before the first step and restored on stop; scenes that were clean beforehand are marked clean
again. The baseline freeze is stored separately from that snapshot, so freezing a swung pose can
never cause it to be restored. The test collider lives on a `HideAndDontSave` object and is
removed from every component's collider list before it is destroyed. Grab and pose go through
the SDK's own grab path, so `Allow Grabbing`, `Allow Posing` and the grab/pose filters behave
exactly as they do in game — and when a component's own setting refuses the grab, the console
says so rather than the tool appearing broken.

Implementation note: this drives the solver through `VRCDynamicsScheduler.PreScheduleDynamics`
and `PhysBoneManager`'s public surface, with two narrow reflection fallbacks
(`GenerateArbitraryChainId`, `EditorSceneManager.ClearSceneDirtiness`) that each degrade with a
warning rather than failing. Clip playback uses the editor's own `AnimationMode`, the same
machinery the Animation window uses to pose a rig outside Play mode.

The feature set here was prompted by Vivid Nightmare's paid "TruePhysbones" tool. That tool's
binary was not decompiled, read, or referenced in any way — this is an original implementation
written against VRChat's public SDK and the capability list TruePhysbones advertises.

## Installing

Via VCC / ALCOM: add `https://dragonboivrc.github.io/physbone-scene-handles/index.json` as a
repository (Settings > Packages > Add Repository), then add "PhysBone Scene Handles" to your
project. Manually: download the `.zip`/`.unitypackage` from the
[latest release](https://github.com/DragonBoiVRC/physbone-scene-handles/releases/latest), or
copy `Packages/com.opensource.physbonehandles` from this repo straight into your project's
`Packages/` folder. Requires the VRChat SDK3 Avatars package (`com.vrchat.base`) — everything
here compiles out (`#if PBHANDLES_VRCSDK_PRESENT`) if it isn't installed.

## Status

Collider/PhysBone scene handles and the Auto Collider Generator have both been used and
iterated on in a real project. The Set Root to Self button (both the inline version and the
context menu fallback) has been tested live, including the Harmony patch it depends on. The
Contact scene handles (`ContactSceneHandles.cs`) have been live-tested for the Sphere shape
(handles, toggle panel, and the rotation-hidden-on-sphere gating all confirmed); Capsule
reuses already-proven code from the collider handles, but Box's face-slider handles have
only been compile-checked, not click-tested, for lack of a Box-shaped Contact to try them
on. The collider picker (`PhysBoneColliderPicker.cs`) is brand new and hasn't been tried
in-editor yet.

Both items that used to be listed here as known weaknesses are now fixed: the chain walk
handles branching chains (`PhysBoneChainUtil.BuildChains` returns every root-to-leaf path),
and non-uniform scale no longer silently uses `lossyScale.x` — it takes the largest axis,
since over-estimating a collider is the safer error.

The newest additions compile clean against Unity 2022.3 plus the real VRChat SDK, but have
**not been click-tested in-editor** yet:

- The custom component UI (`VRCPhysBoneInspector.cs`, `VRCPhysBoneColliderInspector.cs`,
  `InspectorUI.cs`, `CustomEditorOverridePatch.cs`). In 0.6.0 this never drew at all — the SDK's
  own editor won the `[CustomEditor]` race; 0.6.1 adds the override that makes ours win, so the
  layout gets its first real look now. Field names come from VRChat's own serialized YAML,
  so they're exactly what Unity serializes; what hasn't been seen running is the layout —
  the manually-laid-out rows (pill buttons on section headers, the ✐ handle toggles, the
  inline force curves) could be off by a few pixels or crowd each other at a narrow
  inspector width. If something overlaps or clips, that's the thing to report.
- The endpoint drag handle, the multi-strand chain walk, and
  **Mirror Settings to Other Side** (`PhysBoneMirrorUtility.cs`).
- The entire live preview subsystem, new in 0.7.0 (`PhysBonePreview.cs` and the
  `PhysBonePreview*` / `PhysBoneTestMotion` / `PhysBoneClipPlayback` / `PhysBoneCurveMarkers`
  files). The safety path is the part that matters most and the part a compile check cannot
  confirm: snapshot-and-restore is written to put every Transform back, but it has not been
  watched doing it. Start it on a scene you don't mind re-opening the first time, and check the
  bones return exactly where they were when you press Stop & Restore. The panel height is
  computed by hand per visible row, so rows may crowd or clip; grab picking, the drag handle,
  the right-click release, and the inspector 3D viewport's orbit/zoom have never been clicked.

The live preview in 0.7.0 did not actually simulate, and 0.7.1 fixes it. The avatar moved
under a test motion but the bones stayed rigid, because `VRCDynamicsScheduler.PreScheduleDynamics`
does not take a manager — it reads the static `PhysBoneManager.Inst` and skips the PhysBone
pass entirely when that is null. `Inst` is assigned in `Awake`, and `PhysBoneManager` has no
`[ExecuteAlways]`, so in edit mode `Awake` never runs. The preview built a manager, initialised
it and registered every chain with it, and then the solver was never asked to step those chains:
the constraint and contact passes ran, the test motion moved the root, and nothing swung. The
preview now publishes its manager as `Inst` for the duration and restores the previous value on
stop.

0.7.2 fixes a second way the same preview could appear frozen: when an AnimationClip moved the
rig, the cached `Chain.isAnimated` stayed false and `configHasUpdated` stayed false, so
`SolveAnimation` never ran and `UpdateChains` never refreshed the `Bone`/`Chain` copies. Bones
held in world space while the root moved under them. The preview now pushes `isAnimated` and
`configHasUpdated` each step, and stops the clip and the test motion from both writing the same
avatar root. Inspector sliders also take effect live now — dragging Pull, Spring, radius, a
limit angle or any distribution curve updates the running solve via `ApplyConfigurationChanges`
instead of waiting for a stop/start cycle.

Angle limit handles (`PhysBoneLimitSceneHandles.cs`) had a real bug in 0.5.0: the limit
orientation was read from a field named `rotation`, which `VRCPhysBone` doesn't have — that
one belongs to the *collider*. The PhysBone's field is `limitRotation`, and it serializes as
a Vector3 of euler degrees rather than a quaternion, so the lookup always failed and every
cone drew unrotated, silently ignoring whatever Limit Rotation was set. Fixed in 0.6.0. The
cone geometry itself is still a best-reading of VRChat's published docs rather than something
matched against runtime behaviour, so if a limit draws in a direction the bone plainly can't
swing, report it.

Report anything that doesn't feel right and it'll get fixed.

## License

MIT — see `LICENSE`. Do whatever you want with it.
