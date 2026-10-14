# Feature List

Everything PhysBone Scene Handles does, as of **0.7.2**. Free, open source, cross-platform,
no license key and no phone-home check.

Covers four VRChat components: `VRCPhysBone`, `VRCPhysBoneCollider`, `VRCContactSender`,
`VRCContactReceiver`.

---

## 1. Batch scene-view handles

Drag one handle in the Scene view, change your whole selection at once.

| Modifier | Effect |
| --- | --- |
| none | the drag delta is added to every selected component |
| **Alt** | only the one you are dragging changes |
| **Shift** | every selected one is set to the exact value you dragged to |

**On `VRCPhysBoneCollider`:** Radius, Height (capsule only), Position, Rotation (capsule only).

**On `VRCPhysBone`:**
- One radius handle per bone in the chain, sticking out sideways. Dragging shapes
  `radiusCurve` at that point — bulge the middle of a tail, taper the tip, no hand-editing
  of curve keys.
- Branching chains are fully walked. A PhysBone on a Hand draws all five fingers. A bone
  that several strands share gets one handle, not a stack of them fighting over one key.
- **Endpoint Position** gets a drag handle at the chain tip (under the Position toggle), so
  the virtual extra segment is visible instead of three raw numbers.

**On `VRCContactSender` / `VRCContactReceiver`:** Size, Position, Rotation. Both types batch
together, since both are a `ContactBase` underneath. The **Box** shape (which colliders don't
have) gets three pairs of opposing face-center sliders that grow the box symmetrically.
Sphere hides Rotation, because rotation means nothing on a sphere.

**Supporting behaviour:**
- A toggle panel per family switches each handle type on and off — green on, red off.
  Collider/PhysBone panel bottom-right, Contacts bottom-left, so they never overlap.
- While Position or Rotation editing is live, Unity's own Move/Rotate gizmo is hidden, so you
  cannot grab it by mistake and drag the actual bone.
- Non-uniform scale takes the largest axis, not `lossyScale.x` — over-sizing a collider is
  the safer error.

## 2. Angle limit handles

A **Limits** toggle draws a PhysBone's allowed swing region on the bones themselves. Limits
are the hardest part of a PhysBone to set up blind: "Max Angle 45" tells you nothing about
where the bone stops.

- **Angle** — a cone around the bone's rest direction.
- **Polar** — an elliptical cone from both angles, for a joint that swings freely one way and
  barely at all the other.
- **Hinge** — a flat fan in the plane the bone may rotate in.
- Orientation comes from the component's own **Limit Rotation**, so a rotated limit draws
  rotated.
- A grab handle on the first joint's rim edits Max Angle with a live degree readout, same
  Alt / Shift rules as every other handle.
- Fields go through `SerializedObject` by name, so a future SDK rename switches this one
  feature off instead of breaking the package build.

## 3. Custom component UI

A replacement inspector for `VRCPhysBone` and `VRCPhysBoneCollider`, in place of the SDK's.
The stock PhysBone inspector is a flat list of about 40 fields.

- **Eight collapsible sections** — Transforms, Forces, Limits, Collisions, Stretch & Squish,
  Grab & Pose, Options, Gizmos. Expand state lives in the SDK's own `foldout_*` fields, so a
  section you open here is open in the stock inspector too.
- **Arm a handle from its own row.** Rows with a Scene handle carry a small **✐** button:
  Endpoint Position, Limit Type, and the collider's Radius / Height / Position / Rotation.
  One click turns that handle on, no trip to the corner panel.
- **On/off settings ride their section header** as a green/red pill — Allow Collision, Allow
  Grabbing, Show Gizmos, Inside/Outside Bounds.
- **Force curves hidden by default.** All six (Pull, Spring, Stiffness, Gravity, Gravity
  Falloff, Immobile) sit behind an **Advanced** pill, then appear inline beside each value
  rather than on their own rows.
- **Fields that do not apply are not shown.** Max Angle Z only for Polar; a note instead of
  angle fields when Limit Type is None; a Sphere collider hides Height and Rotation.
- **Opt out:** `Tools > PhysBone Handles > Use Custom Component UI`. Off restores VRChat's
  own inspector exactly, and takes effect immediately without reselecting.

## 4. Collider picker

Select PhysBones, turn on **Edit Colliders** (top-right of the Scene view), and every
collider on the same avatar appears as a clickable sphere — green if it is already in the
PhysBone's Colliders list, red if not. Click to toggle, instead of dragging list entries one
at a time. With several PhysBones selected a click applies to all of them; **Alt** affects
only the active one. Avatar scope comes from `VRCAvatarDescriptor`.

## 5. Auto Collider Generator

`Tools > PhysBone Scene Handles > Auto Collider Generator` — fits a collider to each core
Humanoid bone (Hips, Spine, Chest, Shoulders, Upper/Lower Arms, Upper/Lower Legs, Neck, Head).

- Point it at an avatar root with a Humanoid `Animator`. It lists every `SkinnedMeshRenderer`
  with a checkbox so you choose what counts as "the body". This matters: leaving a flared
  skirt checked inflates the Hips collider past the actual body.
- **Radius** is fit from real geometry — the vertices dominantly weighted to each bone,
  recentred on their true centroid, at a percentile set by **Fit Tightness**.
- **Length** comes from the skeleton, bone to child joint, so capsules span the full limb
  even on rigs with twist bones.
- **Radius Margin** inflates every radius so colliders sit just outside the skin.
- A live cyan wireframe preview shows what Apply would create, updating as you drag.
- **Skip bones that already have a Collider** (on by default) leaves hand-tuned ones alone on
  later runs.
- Humanoid rig mapping only — custom jiggle bones are not covered; batch-add those instead.

## 6. Menu commands

`GameObject > PhysBone Handles`:

- **Add VRC Phys Bone Collider to Selection** / **Add VRC Phys Bone to Selection** — adds the
  component to every selected object that lacks one.
- **Copy Collider Settings (Active → Selection)** — copies shape, radius, height and bounds
  behaviour from the active object onto the rest of the selection. Root, position and
  rotation stay per-bone.
- **Mirror Settings to Other Side** — finds each object's opposite-side twin by name
  (`Left`/`Right`, `_L`/`_R`, `.L`/`.R`, `-L`/`-R`, and lowercase), copies the collider or
  contact across, flips position and rotation about X, and repoints a self-referencing
  `rootTransform` at the target. The copy walks `SerializedObject` property by property
  rather than naming fields, so every serialized setting comes over, including ones a future
  SDK adds. Adds the component to the twin if missing. Objects with no clear side marker, or
  no twin in the scene, are skipped and listed in a console warning.

## 7. Set Root to Self

All four components have a Root Transform field that is very often just this same GameObject,
with no one-click way to say so.

- A small **S** button inside the Root Transform row, next to VRChat's own object field. Works
  with several objects selected — each gets its own Transform, not all the same one.
- The same action is in the component's **⋮** context menu as **Set Root to Self**, which does
  not depend on the inline button working.

## 8. Live preview (edit-mode PhysBone simulation)

Runs VRChat's own PhysBone solver on the selected components **in edit mode**, without entering
Play mode and without an upload-and-test loop. Select a PhysBone and the **Live Preview** panel
appears in the bottom-left of the Scene view (`Tools > PhysBone Handles > Show Live Preview
Panel` toggles it).

Nothing on your avatar is changed. Every Transform the solver can touch is snapshotted before
the first step and restored on stop, and scenes that were clean before the preview are marked
clean again afterwards.

- **Start / Stop & Restore.** Registers the selected components with a private solver instance,
  stepped from `EditorApplication.update` on a fixed 1/60 s cadence.
- **Pause, frame-step, reset.** `Step ▸` advances exactly one solver iteration — frame-stepping
  a settling bone is how you see precisely where it overshoots. Reset snaps back to rest and
  keeps simulating from there.
- **Refresh rate: 72 / 90 / 120 / 144 Hz.** The same bone genuinely behaves differently at 72
  and 144, so the rate you actually play at is selectable.
- **Test motions.** Sway, Gust, Circle, Drop, and avatar translation (Walk), with Amount and
  Speed. A bone at rest tells you almost nothing; Pull and Spring only show their character
  under movement. Offsets are applied relative to the captured base pose each step, never
  accumulated.
- **AnimationClip playback.** Drop one of the avatar's own clips in and the chain is tested
  against the motion it will actually see — "the hair clears the shoulder during the emote I'm
  shipping", not "the hair looks fine when it sways". Needs an Animator on the avatar root.
- **Grab and pose testing.** Click a bone to grab it, drag the handle to pull, right-click to
  let go. Goes through the SDK's own grab path, so `Allow Grabbing`, `Allow Posing` and the
  grab/pose filters apply exactly as they do in game — and a refusal is explained in the console
  rather than silently doing nothing. `Pose` leaves the chain posed on release.
- **Test collider.** Spawns a throwaway sphere collider you can drag through the chain, with a
  radius slider. It lives on a `HideAndDontSave` object, is pulled back out of every component's
  collider list before being destroyed, and is never saved to your avatar.
- **Tip trails.** Draws the path each chain's tip has travelled (2 s of history). The shape of
  the path is what tells you whether the damping is right.
- **Baseline freeze (❄).** Freezes the current pose as a ghost outline so a settings change can
  be compared against where the chain used to sit. Stored separately from the restore snapshot,
  so freezing can never cause a swung pose to be restored onto your avatar.
- **Live parameter readout.** `_Angle`, `_Stretch` and `_Squish` as bars plus `_IsGrabbed` /
  `_IsPosed` flags, in the component inspector — the same values your animator parameters see.
- **Inspector 3D viewport.** A `3D` toggle in the inspector renders the chain into a small
  orbitable viewport (drag to orbit, scroll to zoom), so the bone can be watched while the
  fields right below it are being changed.
- **Curve sampling markers.** Pick a force (Pull, Spring, Stiffness, Gravity, Gravity Falloff,
  Immobile, Radius, Max Angle X, Max Stretch) and every bone gets a marker sized and coloured by
  what that force's distribution curve actually resolves to there, optionally with the number
  printed. The curve is sampled by normalized depth **along each strand**, so a hand or
  multi-strand hair shows per-strand values — which is exactly what the inspector's 70-pixel
  curve thumbnail cannot tell you. Works with the preview stopped too.

---

## Requirements

Unity 2019.4 or newer, plus the VRChat SDK3 Avatars package (`com.vrchat.base`). Everything
compiles out behind `PBHANDLES_VRCSDK_PRESENT` if the SDK is absent. Editor-only — nothing
ships in your avatar build.

## What is not verified yet

See the Status section of `README.md`. In short: the handles and the Auto Collider Generator
have been used in a real project; the custom inspector's layout, the endpoint handle, the
multi-strand walk and Mirror Settings compile clean but have not been click-tested. The whole
live preview subsystem (section 8) is new in 0.7.0 and is compile-verified only — the panel
layout, the behaviour in motion, grab/pose interaction and the inspector viewport have not been
click-tested. 0.7.1 fixes the preview not simulating at all: the solver is driven through the
static `PhysBoneManager.Inst`, which only `Awake` sets and which therefore stays null in edit
mode, so the registered chains were never stepped. 0.7.2 fixes a second preview failure where
bones froze while a clip played (`isAnimated` / `configHasUpdated` were never pushed, so
`SolveAnimation` never ran and `UpdateChains` never refreshed the cached `Bone`/`Chain`), stops
the clip and the test motion fighting over the same root transform, and makes inspector edits
take effect live by pushing `ApplyConfigurationChanges` each step.
