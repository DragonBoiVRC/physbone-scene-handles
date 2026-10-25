#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // The "+" next to a PhysBone's Parameter field: adds the animator parameters VRChat
    // derives from that prefix to the avatar's FX controller, so they can be used in transitions.
    internal static class PhysBoneParameterUtility
    {
        private struct Suffix
        {
            internal string name;
            internal AnimatorControllerParameterType type;
        }

        private static readonly Suffix[] Suffixes =
        {
            new Suffix { name = "_IsGrabbed", type = AnimatorControllerParameterType.Bool },
            new Suffix { name = "_IsPosed", type = AnimatorControllerParameterType.Bool },
            new Suffix { name = "_Angle", type = AnimatorControllerParameterType.Float },
            new Suffix { name = "_Stretch", type = AnimatorControllerParameterType.Float },
            new Suffix { name = "_Squish", type = AnimatorControllerParameterType.Float },
        };

        internal static void DrawParameterRow(SerializedObject so, SerializedProperty parameter, string label)
        {
            if (parameter == null) return;

            Rect row = EditorGUILayout.GetControlRect(true, EditorGUI.GetPropertyHeight(parameter, true));
            const float buttonWidth = 22f;
            Rect field = new Rect(row.x, row.y, row.width - buttonWidth - 2f, row.height);
            Rect button = new Rect(row.xMax - buttonWidth, row.y, buttonWidth, EditorGUIUtility.singleLineHeight);

            EditorGUI.PropertyField(field, parameter, new GUIContent(label), true);

            bool hasPrefix = !parameter.hasMultipleDifferentValues && !string.IsNullOrEmpty(parameter.stringValue);
            using (new EditorGUI.DisabledScope(!hasPrefix))
            {
                if (GUI.Button(button, new GUIContent("+",
                        hasPrefix
                            ? "Add this Parameter's _IsGrabbed, _IsPosed, _Angle, _Stretch and _Squish to the avatar's FX controller."
                            : "Type a Parameter name first."), EditorStyles.miniButton))
                {
                    ShowMenu(so.targetObjects, parameter.stringValue);
                }
            }
        }

        private static void ShowMenu(Object[] targets, string prefix)
        {
            List<AnimatorController> controllers = FindFxControllers(targets, out string problem);
            if (controllers.Count == 0)
            {
                EditorUtility.DisplayDialog("Add PhysBone Parameters", problem, "OK");
                return;
            }

            GenericMenu menu = new GenericMenu();
            int missingCount = 0;
            foreach (Suffix s in Suffixes)
                if (MissingFrom(controllers, prefix + s.name)) missingCount++;

            if (missingCount > 0)
                menu.AddItem(new GUIContent("Add All (" + missingCount + " missing)"), false,
                    () => Add(controllers, prefix, Suffixes));
            else
                menu.AddDisabledItem(new GUIContent("Add All (all already present)"));
            menu.AddSeparator("");

            foreach (Suffix s in Suffixes)
            {
                Suffix captured = s;
                string full = prefix + s.name;
                string text = full + "  (" + s.type + ")";
                if (MissingFrom(controllers, full))
                    menu.AddItem(new GUIContent(text), false, () => Add(controllers, prefix, new[] { captured }));
                else
                    menu.AddDisabledItem(new GUIContent(text + " - already in FX"));
            }
            menu.ShowAsContext();
        }

        private static bool MissingFrom(List<AnimatorController> controllers, string name)
        {
            foreach (AnimatorController c in controllers)
                if (!Has(c, name)) return true;
            return false;
        }

        private static bool Has(AnimatorController c, string name)
        {
            foreach (AnimatorControllerParameter p in c.parameters)
                if (p.name == name) return true;
            return false;
        }

        private static void Add(List<AnimatorController> controllers, string prefix, IEnumerable<Suffix> suffixes)
        {
            int added = 0;
            foreach (AnimatorController c in controllers)
            {
                Undo.RecordObject(c, "Add PhysBone Parameters");
                foreach (Suffix s in suffixes)
                {
                    string full = prefix + s.name;
                    if (Has(c, full)) continue;
                    c.AddParameter(new AnimatorControllerParameter { name = full, type = s.type });
                    added++;
                }
                EditorUtility.SetDirty(c);
            }
            if (added > 0)
                Debug.Log("[PhysBone Handles] Added " + added + " parameter(s) with prefix '" + prefix + "' to the FX controller.");
        }

        // One controller per distinct avatar FX layer among the selected PhysBones.
        private static List<AnimatorController> FindFxControllers(Object[] targets, out string problem)
        {
            var result = new List<AnimatorController>();
            problem = null;
            foreach (Object o in targets)
            {
                VRCPhysBone pb = o as VRCPhysBone;
                if (pb == null) continue;

                VRCAvatarDescriptor desc = pb.GetComponentInParent<VRCAvatarDescriptor>();
                if (desc == null)
                {
                    problem = "This PhysBone is not under an avatar (no VRC Avatar Descriptor above it).";
                    continue;
                }

                AnimatorController fx = null;
                if (desc.baseAnimationLayers != null)
                {
                    foreach (var layer in desc.baseAnimationLayers)
                    {
                        if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX) continue;
                        fx = layer.animatorController as AnimatorController;
                        break;
                    }
                }

                if (fx == null)
                {
                    problem = "The avatar's FX layer has no Animator Controller assigned. Assign one on the " +
                              "VRC Avatar Descriptor (Playable Layers > FX, untick Default) and try again.";
                    continue;
                }
                if (AssetDatabase.GetAssetPath(fx).StartsWith("Packages/"))
                {
                    problem = "The FX controller is a read-only package asset. Make a copy of it in your " +
                              "project, assign the copy on the avatar, and try again.";
                    continue;
                }
                if (!result.Contains(fx)) result.Add(fx);
            }
            if (result.Count > 0) problem = null;
            else if (problem == null) problem = "No FX controller found.";
            return result;
        }
    }
}
#endif
