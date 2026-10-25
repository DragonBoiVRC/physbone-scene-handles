#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Replaces VRChat's own VRCPhysBone inspector with a compact, collapsible one, and wires
    // the rows that have a Scene-view handle to the handle's on/off switch.
    //
    // Why replace it at all:
    //   The stock inspector is a flat list of ~40 fields. Setting up a tail means scrolling
    //   past forces to find limits, scrolling back for the radius, and switching to the Scene
    //   view to see if any of it worked. This package already draws handles for radius,
    //   endpoint and limits; the missing half was an inspector that groups the fields and lets
    //   you arm the matching handle from the row you're already looking at.
    //
    // How it stays safe across SDK versions:
    //   Every field is looked up by serialized name through SerializedObject and drawn via
    //   InspectorUI.Optional, which skips anything it can't find. If a future SDK renames a
    //   field, that one row disappears; the rest of the component still draws, and nothing
    //   fails to compile. The SDK's own foldout_* bools are reused where they exist, so a
    //   section expanded here is expanded in the stock inspector too.
    //
    //   The field names come from VRChat's own serialized output (a scene file's YAML for a
    //   VRCPhysBone) rather than from any decompilation, so they are exactly what Unity
    //   serializes - see the package README's provenance note.
    //
    // Opting out:
    //   Tools > PhysBone Handles > Use Custom Component UI. Turning it off makes this editor
    //   fall through to the SDK's own inspector on the next selection change.
    [CustomEditor(typeof(VRCPhysBone))]
    [CanEditMultipleObjects]
    public class VRCPhysBoneInspector : Editor
    {
        private const string AdvancedForcesKey = "OpenSource.PhysBoneHandles.AdvancedForces";

        private SerializedProperty _foldoutTransforms, _foldoutForces, _foldoutLimits;
        private SerializedProperty _foldoutCollision, _foldoutStretchSquish, _foldoutGrabPose;
        private SerializedProperty _foldoutOptions, _foldoutGizmos;

        private void OnEnable()
        {
            _foldoutTransforms = serializedObject.FindProperty("foldout_transforms");
            _foldoutForces = serializedObject.FindProperty("foldout_forces");
            _foldoutLimits = serializedObject.FindProperty("foldout_limits");
            _foldoutCollision = serializedObject.FindProperty("foldout_collision");
            _foldoutStretchSquish = serializedObject.FindProperty("foldout_stretchsquish");
            _foldoutGrabPose = serializedObject.FindProperty("foldout_grabpose");
            _foldoutOptions = serializedObject.FindProperty("foldout_options");
            _foldoutGizmos = serializedObject.FindProperty("foldout_gizmos");
        }

        private SerializedProperty P(string name) => serializedObject.FindProperty(name);

        private readonly List<VRCPhysBone> _previewTargets = new List<VRCPhysBone>();

        // The selected components, for the preview panel. Rebuilt each repaint rather than
        // cached, because multi-selection can change without OnEnable running again.
        private List<VRCPhysBone> PreviewTargets()
        {
            _previewTargets.Clear();
            foreach (Object o in targets)
            {
                VRCPhysBone pb = o as VRCPhysBone;
                if (pb != null)
                    _previewTargets.Add(pb);
            }
            return _previewTargets;
        }

        private static bool AdvancedForces
        {
            get => EditorPrefs.GetBool(AdvancedForcesKey, false);
            set => EditorPrefs.SetBool(AdvancedForcesKey, value);
        }

        public override void OnInspectorGUI()
        {
            if (!CustomInspectorSettings.Enabled)
            {
                // The user turned the custom UI off. Draw Unity's default field list rather
                // than nothing - we can't hand control back to VRChat's editor from inside
                // our own, but this is still a complete, editable component view.
                DrawDefaultInspector();
                return;
            }

            serializedObject.Update();

            // Above the fields on purpose: the live readout is what you watch while changing
            // them, so it should not scroll off the top when a section is expanded.
            PhysBonePreviewInspectorPanel.Draw(PreviewTargets());

            DrawTransformsSection();
            DrawForcesSection();
            DrawLimitsSection();
            DrawCollisionSection();
            DrawStretchSquishSection();
            DrawGrabPoseSection();
            DrawOptionsSection();
            DrawGizmosSection();

            serializedObject.ApplyModifiedProperties();
        }

        // --- Transforms -------------------------------------------------------
        //
        // Root / Endpoint Position / Multi Child Type / Ignore Transforms. Root gets the same
        // "S" (set to self) button the overlay patch adds to the stock inspector, and Endpoint
        // Position gets the Position handle toggle, since this package draws a drag handle for
        // the endpoint at the chain tip.

        private void DrawTransformsSection()
        {
            if (!InspectorUI.SectionHeaderRow("Transforms", _foldoutTransforms,
                    "OpenSource.PhysBoneHandles.Fold.Transforms"))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                // No "S" (set Root to self) button drawn here on purpose: RootFieldOverlayPatch
                // already injects one into every `rootTransform` row on a tracked component,
                // and it hooks the method PropertyField itself goes through - so drawing our
                // own would paint a second button on top of the patch's.
                InspectorUI.Optional(P("rootTransform"), "Root");
                InspectorUI.PropertyRowWithHandleToggle(P("endpointPosition"),
                    new GUIContent("Endpoint Position",
                        "A virtual extra bone segment past the last real bone. The Position " +
                        "handle shows where it lands."),
                    () => PhysBoneColliderSceneHandles.EditPosition,
                    v => PhysBoneColliderSceneHandles.EditPosition = v,
                    "Endpoint");
                InspectorUI.Optional(P("multiChildType"), "Multi Child Type");
                InspectorUI.Optional(P("ignoreTransforms"), "Ignore Transforms");
                InspectorUI.Optional(P("ignoreOtherPhysBones"), "Ignore Other PhysBones");
                InspectorUI.Optional(P("integrationType"), "Integration Type");
            }
        }

        // --- Forces -----------------------------------------------------------
        //
        // Pull / Spring / Stiffness / Gravity / Gravity Falloff / Immobile, each of which has a
        // paired AnimationCurve that distributes the value along the chain. The curves are the
        // noisy part: six extra rows that most setups never touch. They're behind an
        // "Advanced" pill, off by default.

        private void DrawForcesSection()
        {
            bool advanced = AdvancedForces;
            bool expanded = InspectorUI.SectionHeaderRow("Forces", _foldoutForces,
                "OpenSource.PhysBoneHandles.Fold.Forces",
                rect => AdvancedForces = InspectorUI.PillToggle(rect, advanced, "Advanced",
                    "Show the per-chain distribution curve next to each force value."));
            if (!expanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                ForceRow("Pull", "pull", "pullCurve");
                ForceRow("Spring", "spring", "springCurve");
                ForceRow("Stiffness", "stiffness", "stiffnessCurve");
                ForceRow("Gravity", "gravity", "gravityCurve");
                ForceRow("Gravity Falloff", "gravityFalloff", "gravityFalloffCurve");
                InspectorUI.Optional(P("immobileType"), "Immobile Type");
                ForceRow("Immobile", "immobile", "immobileCurve");
            }
        }

        // One force: the value, plus its distribution curve on the same row when Advanced is
        // on. Matches the stock inspector's pairing, just folded into one line.
        private void ForceRow(string label, string valueName, string curveName)
        {
            SerializedProperty value = P(valueName);
            if (value == null) return;

            if (!AdvancedForces)
            {
                EditorGUILayout.PropertyField(value, new GUIContent(label));
                return;
            }

            SerializedProperty curve = P(curveName);
            if (curve == null)
            {
                EditorGUILayout.PropertyField(value, new GUIContent(label));
                return;
            }

            const float curveWidth = 70f;
            Rect row = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            Rect valueRect = new Rect(row.x, row.y, row.width - curveWidth - 4f, row.height);
            Rect curveRect = new Rect(row.xMax - curveWidth, row.y, curveWidth, row.height);

            EditorGUI.PropertyField(valueRect, value, new GUIContent(label));
            EditorGUI.PropertyField(curveRect, curve, GUIContent.none);
        }

        // --- Limits -----------------------------------------------------------
        //
        // The section this package has the most to add to: Limits is where the Scene handles
        // are genuinely the difference between guessing and seeing. The Limit Type row carries
        // the Limits-handle toggle, and the angle fields shown depend on the type, so a Hinge
        // doesn't display a Max Angle Z it ignores.

        private void DrawLimitsSection()
        {
            if (!InspectorUI.SectionHeaderRow("Limits", _foldoutLimits,
                    "OpenSource.PhysBoneHandles.Fold.Limits"))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                SerializedProperty limitType = P("limitType");
                InspectorUI.PropertyRowWithHandleToggle(limitType,
                    new GUIContent("Limit Type",
                        "None / Angle / Hinge / Polar. The Limits handle draws the allowed " +
                        "swing region on the bones."),
                    () => PhysBoneLimitSceneHandles.EditLimits,
                    v => PhysBoneLimitSceneHandles.EditLimits = v,
                    "Limits");

                // enumValueIndex is -1 when the selection mixes types; show everything then
                // rather than hiding fields the user may need.
                int kind = limitType != null ? limitType.enumValueIndex : -1;
                bool mixed = kind < 0;
                bool isAngleOrHinge = mixed || kind == 1 || kind == 2;
                bool isPolar = mixed || kind == 3;

                if (kind == 0 && !mixed)
                {
                    InspectorUI.HelpRow("Limit Type is None - this chain swings freely.");
                    return;
                }

                if (isAngleOrHinge || isPolar)
                    ForceRow("Max Angle X", "maxAngleX", "maxAngleXCurve");
                if (isPolar)
                    ForceRow("Max Angle Z", "maxAngleZ", "maxAngleZCurve");

                InspectorUI.Optional(P("limitRotation"), "Limit Rotation");
                if (AdvancedForces)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        InspectorUI.Optional(P("limitRotationXCurve"), "X Curve");
                        InspectorUI.Optional(P("limitRotationYCurve"), "Y Curve");
                        InspectorUI.Optional(P("limitRotationZCurve"), "Z Curve");
                    }
                }
            }
        }

        // --- Collisions -------------------------------------------------------
        //
        // Allow Collision rides on the header as a green/red pill, matching how the rest of
        // this package signals on/off. Radius carries the Radius-handle toggle, and the
        // Colliders list gets a button for the scene-view collider picker this package already
        // ships - which is a far faster way to populate that list than dragging references in.

        private void DrawCollisionSection()
        {
            SerializedProperty allowCollision = P("allowCollision");
            bool expanded = InspectorUI.SectionHeaderRow("Collisions", _foldoutCollision,
                "OpenSource.PhysBoneHandles.Fold.Collision",
                rect => InspectorUI.PillToggle(rect, allowCollision,
                    allowCollision != null && allowCollision.boolValue ? "Allow Collision" : "No Collision",
                    "Whether this chain collides with PhysBone colliders at all."));
            if (!expanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                InspectorUI.PropertyRowWithHandleToggle(P("radius"),
                    new GUIContent("Radius",
                        "Collision thickness of the chain. The Radius handle shapes the " +
                        "per-bone multiplier curve on top of this value."),
                    () => PhysBoneColliderSceneHandles.EditRadius,
                    v => PhysBoneColliderSceneHandles.EditRadius = v,
                    "Radius");
                if (AdvancedForces)
                    InspectorUI.Optional(P("radiusCurve"), "Radius Curve");

                InspectorUI.Optional(P("colliders"), "Colliders");
                InspectorUI.Optional(P("collisionFilter"), "Collision Filter");
            }
        }

        // --- Stretch & Squish -------------------------------------------------

        private void DrawStretchSquishSection()
        {
            if (!InspectorUI.SectionHeaderRow("Stretch & Squish", _foldoutStretchSquish,
                    "OpenSource.PhysBoneHandles.Fold.StretchSquish"))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                ForceRow("Max Stretch", "maxStretch", "maxStretchCurve");
                ForceRow("Max Squish", "maxSquish", "maxSquishCurve");
                ForceRow("Stretch Motion", "stretchMotion", "stretchMotionCurve");
            }
        }

        // --- Grab & Pose ------------------------------------------------------

        private void DrawGrabPoseSection()
        {
            SerializedProperty allowGrabbing = P("allowGrabbing");
            bool expanded = InspectorUI.SectionHeaderRow("Grab & Pose", _foldoutGrabPose,
                "OpenSource.PhysBoneHandles.Fold.GrabPose",
                rect => InspectorUI.PillToggle(rect, allowGrabbing,
                    allowGrabbing != null && allowGrabbing.boolValue ? "Allow Grabbing" : "No Grabbing",
                    "Whether players can grab this chain."));
            if (!expanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                InspectorUI.Optional(P("grabFilter"), "Grab Filter");
                InspectorUI.Optional(P("allowPosing"), "Allow Posing");
                InspectorUI.Optional(P("poseFilter"), "Pose Filter");
                InspectorUI.Optional(P("snapToHand"), "Snap To Hand");
                InspectorUI.Optional(P("grabMovement"), "Grab Movement");
            }
        }

        // --- Options ----------------------------------------------------------

        private void DrawOptionsSection()
        {
            if (!InspectorUI.SectionHeaderRow("Options", _foldoutOptions,
                    "OpenSource.PhysBoneHandles.Fold.Options"))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                InspectorUI.Optional(P("isAnimated"), "Is Animated");
                InspectorUI.Optional(P("resetWhenDisabled"), "Reset When Disabled");
                PhysBoneParameterUtility.DrawParameterRow(serializedObject, P("parameter"), "Parameter");
            }
        }

        // --- Gizmos -----------------------------------------------------------
        //
        // The SDK's own bone/limit gizmo opacity sliders. Worth keeping reachable: turning the
        // SDK gizmos down is often what makes this package's handles readable on a dense
        // chain, and having both switches in one place saves hunting.

        private void DrawGizmosSection()
        {
            SerializedProperty showGizmos = P("showGizmos");
            bool expanded = InspectorUI.SectionHeaderRow("Gizmos", _foldoutGizmos,
                "OpenSource.PhysBoneHandles.Fold.Gizmos",
                rect => InspectorUI.PillToggle(rect, showGizmos,
                    showGizmos != null && showGizmos.boolValue ? "Show Gizmos" : "Hide Gizmos",
                    "VRChat's own bone and limit gizmos (separate from this package's handles)."));
            if (!expanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                InspectorUI.Optional(P("boneOpacity"), "Bone Opacity");
                InspectorUI.Optional(P("limitOpacity"), "Limit Opacity");
            }
        }
    }
}
#endif
