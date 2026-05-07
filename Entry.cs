using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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
        private string footerCenter = "<3 Nymh -> Jinxxy.com/nymh";
        private string githubLink = "https://github.com/Nymmh/vrc-avatar-controller-cleaner";
        private GUIStyle footerCenterStyle;
        private bool removeUnusedParams = true;
        private bool removeDeadCode = true;
        private bool keepGestureWeights = true;
        private bool confirmChangesReq = true;
        private bool applyCleanedControllerToAvatar = true;
        private string cleanResults;
        private Vector2 cleanResultsScroll;
        private CleanController.Result pendingCleanRes;
        private HashSet<string> parametersToKeep;
        private Vector2 scrollingBoi;

        [MenuItem("Tools/Nymh/Avatar Controller Cleaner")]

        public static void ShowWindow()
        {
            GetWindow<Window>("Avatar Controller Cleaner");
        }

        private void OnEnable()
        {
            var version = typeof(Window).Assembly.GetName().Version;
            versionLabel = version != null ? "V " + version.ToString(3) : "Version Unknown";

            footerCenterStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            footerCenterStyle.normal.textColor = Color.white;
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
            if (EditorGUI.EndChangeCheck())
            {
                ResetPending();
                if (avatar != null) PopulateFxController();
            }

            EditorGUI.BeginChangeCheck();

            fxController = (AnimatorController)EditorGUILayout.ObjectField("FX Controller", fxController, typeof(AnimatorController), false);
            if (EditorGUI.EndChangeCheck())
            {
                ResetPending();
            }

            GUILayout.Space(10);

            // toggle row
            keepGestureWeights = EditorGUILayout.ToggleLeft("Keep Gesture Weights", keepGestureWeights);
            removeUnusedParams = EditorGUILayout.ToggleLeft("Remove Unused Parameters", removeUnusedParams);
            if (removeUnusedParams)
            {
                EditorGUI.indentLevel++;
                confirmChangesReq = EditorGUILayout.ToggleLeft("Confirm Changes Before Removing", confirmChangesReq);
                EditorGUI.indentLevel--;
            }
            removeDeadCode = EditorGUILayout.ToggleLeft("Remove Dead Code", removeDeadCode);
            applyCleanedControllerToAvatar = EditorGUILayout.ToggleLeft("Apply Cleaned Controller to Avatar", applyCleanedControllerToAvatar);
            GUILayout.Space(10);

            if (pendingCleanRes != null)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Confirm")) ConfirmClean();
                if (GUILayout.Button("Cancel")) CancelClean();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                using (new EditorGUI.DisabledScope(!removeUnusedParams && !removeDeadCode))
                {
                    if (GUILayout.Button("Clean"))
                    {
                        OnClean();
                    }
                }
            }

            // Fk this thing 
            // Chud dynamic content scrolling area
            if (!string.IsNullOrEmpty(cleanResults))
            {
                GUILayout.Space(10);
                GUILayout.Label("Clean successful! This is what was removed:", EditorStyles.boldLabel);
                cleanResultsScroll = EditorGUILayout.BeginScrollView(cleanResultsScroll, GUILayout.ExpandHeight(true));
                EditorGUILayout.TextArea(cleanResults, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
            else if (pendingCleanRes != null)
            {
                GUILayout.Space(10);
                GUILayout.Label("Confirm parameters to remove:", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
                scrollingBoi = EditorGUILayout.BeginScrollView(scrollingBoi, GUILayout.ExpandHeight(true));

                for (int i = 0; i < pendingCleanRes.RemovedNamed.Count; i++)
                {
                    var param = pendingCleanRes.RemovedNamed[i];

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(param, GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("Keep", GUILayout.Width(50)))
                    {
                        parametersToKeep.Add(param);
                        pendingCleanRes.RemovedNamed.RemoveAt(i);
                        Repaint();
                        EditorGUILayout.EndHorizontal();
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
            }
            else
            {
                GUILayout.FlexibleSpace();
            }

            var footerRect = EditorGUILayout.GetControlRect(false);
            GUI.Label(footerRect, versionLabel, EditorStyles.miniLabel);
            GUI.Label(footerRect, footerCenter, footerCenterStyle);

            var copyButtonRect = new Rect(footerRect.xMax - 130f, footerRect.y, 130f, footerRect.height);
            if (GUI.Button(copyButtonRect, "Copy github link"))
            {
                EditorGUIUtility.systemCopyBuffer = githubLink;
            }
        }


        private void OnClean() 
        {
            cleanResults = null;
            pendingCleanRes = null;
            parametersToKeep = new HashSet<string>();

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

            if (removeUnusedParams && confirmChangesReq && res.RemovedNamed.Count > 0)
            {
                pendingCleanRes = res;
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

            cleanResults = sb.ToString();

            if (applyCleanedControllerToAvatar)
            {
                ApplyCleanedFxToAvatar();
            }

            EditorUtility.DisplayDialog("Clean Complete", "Clean successful", "OK");
        }

        // Actual hell on earth fricking Frijas requested this
        
        private void ConfirmClean()
        {
            if (pendingCleanRes == null)
            {
                return;
            }

            var res = pendingCleanRes;

            if (parametersToKeep.Count > 0)
            {
                var srcPath = AssetDatabase.GetAssetPath(fxController);

                if (!string.IsNullOrEmpty(srcPath))
                {
                    var dir = Path.GetDirectoryName(srcPath)?.Replace('\\', '/');
                    var baseName = Path.GetFileNameWithoutExtension(srcPath);

                    if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(baseName))
                    {
                        var cleanedPath = dir + "/" + baseName + ".Cleaned.controller";
                        var cleanedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(cleanedPath);

                        if (cleanedController != null && fxController != null)
                        {

                            var originalParams = fxController.parameters;
                            var cleanedByName = new Dictionary<string, AnimatorControllerParameter>();

                            foreach (var param in cleanedController.parameters)
                            {
                                if (!cleanedByName.ContainsKey(param.name))
                                    cleanedByName[param.name] = param;
                            }

                            var orderedParams = new List<AnimatorControllerParameter>();

                            foreach (var origParam in originalParams)
                            {
                                if (cleanedByName.TryGetValue(origParam.name, out var cleanedParam))
                                {
                                    orderedParams.Add(cleanedParam);
                                }
                                else if (parametersToKeep.Contains(origParam.name))
                                {
                                    orderedParams.Add(origParam);
                                }
                            }

                            foreach (var param in cleanedController.parameters)
                            {
                                if (!orderedParams.Any(p => p.name == param.name))
                                {
                                    orderedParams.Add(param);
                                }
                            }

                            cleanedController.parameters = orderedParams.ToArray();
                            EditorUtility.SetDirty(cleanedController);
                            AssetDatabase.SaveAssets();
                        }
                    }
                }
            }

            var sb = new StringBuilder();
            var actuallyRemoved = res.RemovedNamed;

            if (actuallyRemoved.Count > 0)
            {
                sb.AppendLine($"Removed {actuallyRemoved.Count} unused parameter(s):");

                foreach (var name in actuallyRemoved)
                {
                    sb.AppendLine(" - " + name);
                }
            }

            if (res.GhostParams.Count > 0)
            {
                if (actuallyRemoved.Count > 0) sb.AppendLine();

                sb.AppendLine($"Cleaned {res.GhostParams.Count} dead reference(s):");

                foreach (var name in res.GhostParams)
                {
                    sb.AppendLine(" - " + name);
                }
            }

            cleanResults = sb.ToString();
            pendingCleanRes = null;
            parametersToKeep = new HashSet<string>();

            if (applyCleanedControllerToAvatar)
            {
                ApplyCleanedFxToAvatar();
            }

            EditorUtility.DisplayDialog("Clean Complete", "Clean successful", "OK");
        }

        // Reset stuff for memory
        private void CancelClean()
        {
            var srcPath = AssetDatabase.GetAssetPath(fxController);
            if (!string.IsNullOrEmpty(srcPath))
            {
                var dir = Path.GetDirectoryName(srcPath)?.Replace('\\', '/');
                var baseName = Path.GetFileNameWithoutExtension(srcPath);

                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(baseName))
                {

                    var cleanedPath = dir + "/" + baseName + ".Cleaned.controller";

                    if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(cleanedPath)))
                    {
                        AssetDatabase.DeleteAsset(cleanedPath);
                    }
                }
            }

            pendingCleanRes = null;
            parametersToKeep = new HashSet<string>();
            cleanResults = null;
        }

        private void ResetPending()
        {
            if (pendingCleanRes != null)
            {
                CancelClean();
            }
            else
            {
                cleanResults = null;
            }
        }

        private void ApplyCleanedFxToAvatar()
        {
            if (avatar == null) return;

            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return;

            var srcPath = AssetDatabase.GetAssetPath(fxController);
            if (string.IsNullOrEmpty(srcPath))
            {
                return;
            }

            var dir = Path.GetDirectoryName(srcPath)?.Replace('\\', '/');
            var baseName = Path.GetFileNameWithoutExtension(srcPath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName))
            {
                return;
            }

            var cleanedPath = dir + "/" + baseName + ".Cleaned.controller";
            var cleanedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(cleanedPath);
            if (cleanedController == null) return;

            var layers = descriptor.baseAnimationLayers;

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type != AnimLayerType.FX) continue;

                layers[i].isDefault = false;
                layers[i].animatorController = cleanedController;
                descriptor.baseAnimationLayers = layers;
                EditorUtility.SetDirty(descriptor);
                fxController = cleanedController;
                return;
            }
        }
    }
}
