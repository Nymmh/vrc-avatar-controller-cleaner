using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using AnimLayerType = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType;

namespace vrc_avatar_controller_cleaner
{
    public class Window : EditorWindow
    {
        private GameObject avatar;
        private AnimatorController fxController;
        private string versionLabel;
        private string footerCopy = " <3 Nymh -> jinxxy.com/nymh";
        private bool removeUnusedParams = true;
        private bool removeDeadCode = true;
        private bool keepGestureWeights = true;

        [MenuItem("Tools/Nymh/Avatar Controller Cleaner")]

        public static void ShowWindow()
        {
            GetWindow<Window>("Avatar Controller Cleaner");
        }

        private void OnEnable()
        {
            var version = typeof(Window).Assembly.GetName().Version;
            versionLabel = version != null ? "V " + version.ToString() : "Version Unknown";
        }

        private void PopulateFxController()
        {
            fxController = null;
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return;

            foreach (var l in descriptor.baseAnimationLayers)
            {
                if (l.type != AnimLayerType.FX) continue;
                if (!l.isDefault && l.animatorController != null)
                {
                    fxController = l.animatorController as AnimatorController;
                    break;
                }
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("Avatar Controller Cleaner", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUI.BeginChangeCheck();
            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && avatar != null)
            {
                PopulateFxController();
            }

            fxController = (AnimatorController)EditorGUILayout.ObjectField("Fx Controller", fxController, typeof(AnimatorController), false);
            GUILayout.Space(10);

            keepGestureWeights = EditorGUILayout.ToggleLeft("Keep gesture weights", keepGestureWeights);
            removeUnusedParams = EditorGUILayout.ToggleLeft("Remove unused params", removeUnusedParams);
            removeDeadCode = EditorGUILayout.ToggleLeft("Remove dead code", removeDeadCode);
            GUILayout.Space(10);

            using (new EditorGUI.DisabledScope(!removeUnusedParams && !removeDeadCode))
            {
                if (GUILayout.Button("Clean"))
                {
                    OnClean();
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(versionLabel +" "+ footerCopy, EditorStyles.centeredGreyMiniLabel);
        }


        private void OnClean() 
        {
            if(fxController == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign a valid FX Controller", "OK");
                return;
            }

            var res = CleanController.Run(fxController, removeUnusedParams, removeDeadCode, keepGestureWeights);

            if (!res.Success)
            {
                EditorUtility.DisplayDialog("Error", res.Msg, "OK");
                return;
            }

            if(res.Removed == 0 && res.GhostParams.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing to Clean", "There was nothing to be cleaned", "OK");
                return;
            }

            var sb = new StringBuilder();

            if (res.Removed > 0)
            {
                sb.AppendLine($"Removed {res.Removed} unused parameter(s):");
                foreach (var name in res.RemovedNamed)
                {
                    sb.AppendLine(" - " + name);
                }
            }

            if (res.GhostParams.Count > 0)
            {
                if (res.Removed > 0) sb.AppendLine();
                sb.AppendLine($"Cleaned {res.GhostParams.Count} dead parameter reference(s):");
                foreach (var name in res.GhostParams)
                {
                    sb.AppendLine(" - " + name);
                }
            }

            EditorUtility.DisplayDialog("Clean Complete", sb.ToString(), "OK");
        }
    }
}
