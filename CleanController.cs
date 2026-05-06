using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace vrc_avatar_controller_cleaner
{
    internal static class CleanController
    {
        private const string ControllerExtention = ".Cleaned.controller";

        public class Result
        {
            public bool Success;
            public string Msg;
            public int Removed;
            public List<string> RemovedNamed = new List<string>();
            public List<string> GhostParams = new List<string>();
        }

        private static Result Fail(string msg) => new Result { Success = false, Msg = msg };

        private static void GetFromConditions(AnimatorCondition[] conditions, HashSet<string> names)
        {
            foreach (var c in conditions)
            {
                if (!string.IsNullOrEmpty(c.parameter))
                {
                    names.Add(c.parameter);
                }
            }
        }

        private static void GetFromBlendTree(UnityEditor.Animations.BlendTree bt, HashSet<string> names)
        {
            if (bt == null) return;
            if (!string.IsNullOrEmpty(bt.blendParameter))
            {
                names.Add(bt.blendParameter);
            }
            if (!string.IsNullOrEmpty(bt.blendParameterY))
            {
                names.Add(bt.blendParameterY);
            }

            foreach (var c in bt.children)
            {
                if (!string.IsNullOrEmpty(c.directBlendParameter))
                {
                    names.Add(c.directBlendParameter);
                }
                if(c.motion is UnityEditor.Animations.BlendTree cBt)
                {
                    GetFromBlendTree(cBt, names);
                }
            }
        }

        // Actually a pain in the ass
        private static void CleanGhostRefsFromBlendTree(UnityEditor.Animations.BlendTree bt, HashSet<string> ghostParams)
        {
            if (bt == null) return;

            var so = new SerializedObject(bt);
            bool changed = false;

            var blendParamProp = so.FindProperty("m_BlendParameter");
            if (blendParamProp != null && !string.IsNullOrEmpty(blendParamProp.stringValue) && ghostParams.Contains(blendParamProp.stringValue))
            {
                blendParamProp.stringValue = string.Empty;
                changed = true;
            }

            var blendParamYProp = so.FindProperty("m_BlendParameterY");
            if (blendParamYProp != null && !string.IsNullOrEmpty(blendParamYProp.stringValue) && ghostParams.Contains(blendParamYProp.stringValue))
            {
                blendParamYProp.stringValue = string.Empty;
                changed = true;
            }

            var childsProp = so.FindProperty("m_Childs");
            if (childsProp != null && childsProp.isArray)
            {
                for (int i = 0; i < childsProp.arraySize; i++)
                {
                    var child = childsProp.GetArrayElementAtIndex(i);
                    var dbpProp = child.FindPropertyRelative("m_DirectBlendParameter");
                    if (dbpProp != null && !string.IsNullOrEmpty(dbpProp.stringValue) && ghostParams.Contains(dbpProp.stringValue))
                    {
                        dbpProp.stringValue = string.Empty;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(bt);
            }

            foreach (var c in bt.children)
            {
                if (c.motion is UnityEditor.Animations.BlendTree cBt)
                {
                    CleanGhostRefsFromBlendTree(cBt, ghostParams);
                }
            }
        }

        private static bool CleanConditionsOnTransition(AnimatorTransitionBase t, HashSet<string> ghostParams)
        {
            var so = new SerializedObject(t);
            var condsProp = so.FindProperty("m_Conditions");
            if (condsProp == null || !condsProp.isArray) return false;

            bool changed = false;
            for (int i = condsProp.arraySize - 1; i >= 0; i--)
            {
                var cond = condsProp.GetArrayElementAtIndex(i);
                var paramProp = cond.FindPropertyRelative("m_ConditionEvent");
                if (paramProp != null && ghostParams.Contains(paramProp.stringValue))
                {
                    condsProp.DeleteArrayElementAtIndex(i);
                    changed = true;
                }
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(t);
            }
            return changed;
        }

        private static void CleanGhostRefsFromDriver(VRCAvatarParameterDriver driver, HashSet<string> ghostParams)
        {
            var before = driver.parameters.Count;
            driver.parameters = driver.parameters.Where(p => !ghostParams.Contains(p.name) && !ghostParams.Contains(p.source)).ToList();
            if (driver.parameters.Count != before)
            {
                EditorUtility.SetDirty(driver);
            }
        }

        private static void CleanGhostRefsFromStateMachine(AnimatorStateMachine sm, HashSet<string> ghostParams)
        {
            foreach (var t in sm.anyStateTransitions)
            {
                CleanConditionsOnTransition(t, ghostParams);
            }

            foreach (var t in sm.entryTransitions)
            {
                CleanConditionsOnTransition(t, ghostParams);
            }

            foreach (var si in sm.states)
            {
                var state = si.state;
                if (state == null) continue;

                foreach (var t in state.transitions)
                {
                    CleanConditionsOnTransition(t, ghostParams);
                }

                foreach (var b in state.behaviours)
                {
                    if (b is VRCAvatarParameterDriver driver)
                    {
                        CleanGhostRefsFromDriver(driver, ghostParams);
                    }
                }

                if (state.motion is UnityEditor.Animations.BlendTree bt)
                {
                    CleanGhostRefsFromBlendTree(bt, ghostParams);
                }
            }

            foreach (var c in sm.stateMachines)
            {
                if (c.stateMachine != null)
                {
                    CleanGhostRefsFromStateMachine(c.stateMachine, ghostParams);
                }
            }
        }

        private static void CleanAllSubAssets(string assetPath, HashSet<string> ghostParams)
        {
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (var obj in subAssets)
            {
                if (obj == null) continue;

                if (obj is UnityEditor.Animations.BlendTree bt)
                {
                    CleanGhostRefsFromBlendTree(bt, ghostParams);
                }
                else if (obj is AnimatorTransitionBase tr)
                {
                    CleanConditionsOnTransition(tr, ghostParams);
                }
                else if (obj is VRCAvatarParameterDriver drv)
                {
                    CleanGhostRefsFromDriver(drv, ghostParams);
                }
            }
        }
        private static void GetFromStateMachine(AnimatorStateMachine sm, HashSet<string> names)
        {
            foreach (var t in sm.anyStateTransitions)
            {
                GetFromConditions(t.conditions, names);
            }

            foreach (var t in sm.entryTransitions)
            {
                GetFromConditions(t.conditions, names);
            }

            foreach (var si in sm.states)
            {
                var state = si.state;
                if (state == null) continue;

                foreach (var t in state.transitions)
                {
                    GetFromConditions(t.conditions, names);
                }

                foreach (var b in state.behaviours)
                {
                    if (b is VRCAvatarParameterDriver driver)
                    {
                        foreach (var p in driver.parameters)
                        {
                            if (!string.IsNullOrEmpty(p.name)){
                                names.Add(p.name);
                            }

                            if (!string.IsNullOrEmpty(p.source))
                            {
                                names.Add(p.source);
                            }
                        }
                    }
                }

                if (state.motion is UnityEditor.Animations.BlendTree bt)
                {
                    GetFromBlendTree(bt, names);
                }
            }

            foreach (var c in sm.stateMachines)
            {
                if(c.stateMachine != null)
                {
                    GetFromStateMachine(c.stateMachine, names);
                }
            }
        }

        private static HashSet<string> GetUsedParams(UnityEditor.Animations.AnimatorController controller)
        {
            var used = new HashSet<string>();
            foreach (var l in controller.layers)
            {
                if(l.stateMachine != null)
                {
                    GetFromStateMachine(l.stateMachine, used);
                }
            }
            return used;
        }

        // God forbid this breaks again ;-;
        // Have to collect refs to fix the issue of dead code
        // we love unity smile
        private static HashSet<string> GetAllReferencedParams(string assetPath)
        {
            var refs = new HashSet<string>();
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);

            foreach (var obj in subAssets)
            {
                if (obj == null) continue;

                if (obj is UnityEditor.Animations.BlendTree bt)
                {
                    if (!string.IsNullOrEmpty(bt.blendParameter)) refs.Add(bt.blendParameter);
                    if (!string.IsNullOrEmpty(bt.blendParameterY)) refs.Add(bt.blendParameterY);
                    foreach (var c in bt.children)
                    {
                        if (!string.IsNullOrEmpty(c.directBlendParameter))
                        {
                            refs.Add(c.directBlendParameter);
                        }
                    }
                }
                else if (obj is AnimatorTransitionBase tr)
                {
                    foreach (var c in tr.conditions)
                    {
                        if (!string.IsNullOrEmpty(c.parameter)) refs.Add(c.parameter);
                    }
                }
                else if (obj is AnimatorState st)
                {
                    if (!string.IsNullOrEmpty(st.speedParameter)) refs.Add(st.speedParameter);
                    if (!string.IsNullOrEmpty(st.mirrorParameter)) refs.Add(st.mirrorParameter);
                    if (!string.IsNullOrEmpty(st.cycleOffsetParameter)) refs.Add(st.cycleOffsetParameter);
                    if (!string.IsNullOrEmpty(st.timeParameter)) refs.Add(st.timeParameter);
                }
                else if (obj is VRCAvatarParameterDriver drv)
                {
                    foreach (var p in drv.parameters)
                    {
                        if (!string.IsNullOrEmpty(p.name)) refs.Add(p.name);
                        if (!string.IsNullOrEmpty(p.source)) refs.Add(p.source);
                    }
                }
            }
            return refs;
        }

        private static readonly string[] GestureWeightParams = new[] { "GestureLeftWeight", "GestureRightWeight" };

        public static Result Run(UnityEditor.Animations.AnimatorController controller, bool removeUnusedParams, bool removeDeadCode, bool keepGestureWeights)
        {
            if (!removeUnusedParams && !removeDeadCode)
            {
                return Fail("Select at least one option to clean");
            }

            string srcPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(srcPath))
            {
                return Fail("Could not determin controller path");
            }

            string dir = Path.GetDirectoryName(srcPath).Replace('\\', '/');
            string baseName = Path.GetFileNameWithoutExtension(srcPath);
            string outputAsset = dir + '/' + baseName + ControllerExtention;

            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(outputAsset)))
            {
                AssetDatabase.DeleteAsset(outputAsset);
            }

            if (!AssetDatabase.CopyAsset(srcPath, outputAsset))
            {
                return Fail("Failed to copy controller to output");
            }

            AssetDatabase.ImportAsset(outputAsset);
            var copy = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(outputAsset);

            if(copy == null)
            {
                AssetDatabase.DeleteAsset(outputAsset);
                return Fail("Could not load the copied controller");
            }

            var allParams = copy.parameters;
            var definedParamNames = new HashSet<string>(allParams.Select(p => p.name));
            var keepParams = new List<UnityEngine.AnimatorControllerParameter>();
            var removedNames = new List<string>();
            var ghostParamList = new List<string>();
            HashSet<string> ghostParams = null;

            if (removeUnusedParams)
            {
                var usedParams = GetUsedParams(copy);
                if (keepGestureWeights)
                {
                    foreach (var g in GestureWeightParams) usedParams.Add(g);
                }
                foreach (var p in allParams)
                {
                    if (usedParams.Contains(p.name))
                    {
                        keepParams.Add(p);
                    }
                    else
                    {
                        removedNames.Add(p.name);
                    }
                }
            }

            if (removeDeadCode)
            {
                var allReferencedParams = GetAllReferencedParams(outputAsset);
                ghostParams = new HashSet<string>(allReferencedParams.Where(p => !definedParamNames.Contains(p)));
                ghostParamList = ghostParams.OrderBy(p => p).ToList();
            }

            bool anythingChanged = removedNames.Count > 0 || ghostParamList.Count > 0;

            if (!anythingChanged)
            {
                AssetDatabase.DeleteAsset(outputAsset);
                return new Result
                {
                    Success = true,
                    Removed = 0
                };
            }

            if (removeUnusedParams && removedNames.Count > 0)
            {
                copy.parameters = keepParams.ToArray();
            }

            if (removeDeadCode && ghostParamList.Count > 0)
            {
                foreach (var l in copy.layers)
                {
                    if (l.stateMachine != null)
                    {
                        CleanGhostRefsFromStateMachine(l.stateMachine, ghostParams);
                    }
                }

                CleanAllSubAssets(outputAsset, ghostParams);
            }

            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(outputAsset, ImportAssetOptions.ForceUpdate);

            return new Result
            {
                Success = true,
                Removed = removedNames.Count,
                RemovedNamed = removedNames,
                GhostParams = ghostParamList
            };
        }
    }
}
