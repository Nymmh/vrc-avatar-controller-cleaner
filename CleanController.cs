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

        public struct CleanOptions
        {
            public bool RemoveUnusedParams;
            public bool RemoveDeadCode;
            public bool KeepGestureWeights;
            public bool RemoveUnusedAnimationEvents;
            public bool RemoveAllAnimationEvents;
            public bool CopyAnimationFiles;
        }

        public class Result
        {
            public bool Success;
            public string Msg;
            public int Removed;
            public int RemovedAnimationEvents;
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

        private static void GetFromState(AnimatorState state, HashSet<string> names)
        {
            if (state == null) return;

            if (state.speedParameterActive && !string.IsNullOrEmpty(state.speedParameter))
            {
                names.Add(state.speedParameter);
            }

            if (state.mirrorParameterActive && !string.IsNullOrEmpty(state.mirrorParameter))
            {
                names.Add(state.mirrorParameter);
            }

            // hi <3
            if (state.timeParameterActive && !string.IsNullOrEmpty(state.timeParameter))
            {
                names.Add(state.timeParameter);
            }

            if (state.cycleOffsetParameterActive && !string.IsNullOrEmpty(state.cycleOffsetParameter))
            {
                names.Add(state.cycleOffsetParameter);
            }

            if (state.motion is UnityEditor.Animations.BlendTree bt)
            {
                GetFromBlendTree(bt, names);
            }

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
                        if (!string.IsNullOrEmpty(p.name))
                        {
                            names.Add(p.name);
                        }

                        if (!string.IsNullOrEmpty(p.source))
                        {
                            names.Add(p.source);
                        }
                    }
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

        private static long GetInternalFileId(SerializedProperty prop)
        {
            if (prop == null)
            {
                return 0;
            }

            var fileIdProp = prop.FindPropertyRelative("m_FileID") ?? prop.FindPropertyRelative("fileID");
            if (fileIdProp != null && fileIdProp.propertyType == SerializedPropertyType.Integer)
            {
                return fileIdProp.longValue;
            }

            return 0;
        }

        private static bool CleanBrokenRefs(
            AnimatorTransitionBase t,
            HashSet<long> stateIds,
            HashSet<long> smIds)
        {
            var so = new SerializedObject(t);
            bool changed = false;
            // stupid dstState is so dumb
            var dstState = so.FindProperty("m_DstState");
            if (dstState != null && dstState.objectReferenceValue == null)
            {
                var dstId = GetInternalFileId(dstState);
                if (dstId != 0 && !stateIds.Contains(dstId))
                {
                    dstState.objectReferenceValue = null;
                    changed = true;
                }
            }

            var dstSmProp = so.FindProperty("m_DstStateMachine");
            if (dstSmProp != null && dstSmProp.objectReferenceValue == null)
            {
                var dstSmId = GetInternalFileId(dstSmProp);
                if (dstSmId != 0 && !smIds.Contains(dstSmId))
                {
                    dstSmProp.objectReferenceValue = null;
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

        private static void CleanGhostRefsFromStateMachine(
            AnimatorStateMachine sm,
            HashSet<string> ghostParams,
            HashSet<long> sIds,
            HashSet<long> smIds)
        {
            foreach (var t in sm.anyStateTransitions)
            {
                CleanConditionsOnTransition(t, ghostParams);
                CleanBrokenRefs(t, sIds, smIds);
            }

            foreach (var t in sm.entryTransitions)
            {
                CleanConditionsOnTransition(t, ghostParams);
                CleanBrokenRefs(t, sIds, smIds);
            }

            foreach (var si in sm.states)
            {
                var state = si.state;
                if (state == null) continue;

                foreach (var t in state.transitions)
                {
                    CleanConditionsOnTransition(t, ghostParams);
                    CleanBrokenRefs(t, sIds, smIds);
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
                    CleanGhostRefsFromStateMachine(c.stateMachine, ghostParams, sIds, smIds);
                }
            }
        }

        private static void GetDestIds(
            string assetPath,
            out HashSet<long> sIds,
            out HashSet<long> smIds)
        {
            sIds = new HashSet<long>();
            smIds = new HashSet<long>();

            var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (var obj in subAssets)
            {
                if (obj == null) continue;

                // Chud code
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out _, out long localId) || localId == 0)
                {
                    continue;
                }

                if (obj is AnimatorState)
                {
                    sIds.Add(localId);
                }
                else if (obj is AnimatorStateMachine)
                {
                    smIds.Add(localId);
                }
            }
        }

        private static void CleanAllSubAssets(
            string assetPath,
            HashSet<string> ghostParams,
            HashSet<long> sIds,
            HashSet<long> smIds)
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
                    CleanBrokenRefs(tr, sIds, smIds);
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

                GetFromState(state, names);
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

        private static void GetAnimationsFromMotion(Motion m, HashSet<AnimationClip> clips)
        {
            if (m == null) return;
            if (m is AnimationClip clip)
                clips.Add(clip);
            else if (m is UnityEditor.Animations.BlendTree bt)
                foreach (var child in bt.children)
                    GetAnimationsFromMotion(child.motion, clips);
        }

        private static void GetAnimationsFromStateMachine(AnimatorStateMachine sm, HashSet<AnimationClip> clips)
        {
            foreach (var si in sm.states)
                if (si.state?.motion != null)
                    GetAnimationsFromMotion(si.state.motion, clips);
            foreach (var c in sm.stateMachines)
                if (c.stateMachine != null)
                    GetAnimationsFromStateMachine(c.stateMachine, clips);
        }

        private static int RemoveAnimationEvents(UnityEditor.Animations.AnimatorController controller, bool removeUnused, bool removeAll, bool copyFiles)
        {
            if (controller == null) return 0;

            var clips = new HashSet<AnimationClip>();
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine != null)
                    GetAnimationsFromStateMachine(layer.stateMachine, clips);
            }

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                var assetPath = AssetDatabase.GetAssetPath(clip);
                if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seenPaths.Add(assetPath)) continue;
                removed += ProcessAnimFileEvents(assetPath, removeUnused, removeAll, copyFiles);
            }
            return removed;
        }

        // Unity is chud holy shit this is so dumb
        private static int ProcessAnimFileEvents(string assetPath, bool removeUnused, bool removeAll, bool copyFiles)
        {
            var dataPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                assetPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(dataPath)) return 0;
            var rawText = File.ReadAllText(dataPath, Encoding.UTF8);
            var usesCRLF = rawText.Contains("\r\n");
            var lines = rawText.Replace("\r\n", "\n").Split('\n');
            var output = new List<string>(lines.Length);
            int removed = 0;
            int i = 0;

            while (i < lines.Length)
            {
                var line = lines[i];
                var trimmed = line.TrimEnd();

                if (trimmed.EndsWith("m_Events:") && !trimmed.EndsWith("[]"))
                {
                    int eventsIndent = trimmed.Length - "m_Events:".Length;
                    var entryPrefix = new string(' ', eventsIndent) + "- ";
                    var contPrefix  = new string(' ', eventsIndent + 2);

                    int peek = i + 1;
                    while (peek < lines.Length && lines[peek].TrimEnd() == "") peek++;

                    if (peek < lines.Length && lines[peek].StartsWith(entryPrefix))
                    {
                        var eventBlocks = new List<(List<string> blockLines, bool hasFunc)>();
                        int j = i + 1;

                        while (j < lines.Length && (lines[j].StartsWith(entryPrefix) || (lines[j].TrimEnd() == "" && j + 1 < lines.Length && lines[j + 1].StartsWith(entryPrefix))))
                        {
                            if (lines[j].TrimEnd() == "") { j++; continue; }
                            var block = new List<string> { lines[j] };
                            j++;

                            while (j < lines.Length && lines[j].StartsWith(contPrefix) && !lines[j].StartsWith(entryPrefix))
                            {
                                block.Add(lines[j]);
                                j++;
                            }

                            // Extract
                            bool hf = false;
                            foreach (var bl in block)
                            {
                                var t = bl.TrimStart();
                                if (t.StartsWith("functionName:"))
                                {
                                    var val = t.Substring("functionName:".Length).Trim();
                                    hf = val.Length > 0;
                                    break;
                                }
                            }

                            eventBlocks.Add((block, hf));
                        }

                        var kept = new List<(List<string>, bool)>();

                        if (removeAll)
                        {
                            removed += eventBlocks.Count;
                        }
                        else
                        {
                            foreach (var eb in eventBlocks)
                            {
                                if (removeUnused && !eb.hasFunc)
                                    removed++;
                                else
                                    kept.Add(eb);
                            }
                        }

                        if (kept.Count == 0)
                        {
                            output.Add(new string(' ', eventsIndent) + "m_Events: []");
                        }
                        else
                        {
                            output.Add(line);
                            foreach (var kb in kept)
                                foreach (var kl in kb.Item1)
                                    output.Add(kl);
                        }

                        i = j;
                        continue;
                    }
                }

                output.Add(line);
                i++;
            }

            if (removed > 0)
            {
                if (copyFiles)
                {
                    var backupF = Path.Combine(Path.GetDirectoryName(dataPath), "backup");
                    Directory.CreateDirectory(backupF);
                    var backupPath = Path.Combine(backupF, Path.GetFileName(dataPath));
                    if (!File.Exists(backupPath))
                        File.Copy(dataPath, backupPath);
                }

                var sep = usesCRLF ? "\r\n" : "\n";
                File.WriteAllText(dataPath, string.Join(sep, output), Encoding.UTF8);
                AssetDatabase.ImportAsset(assetPath);
            }

            return removed;
        }

        public static Result Run(UnityEditor.Animations.AnimatorController controller, CleanOptions opts)
        {
            if (!opts.RemoveUnusedParams && !opts.RemoveDeadCode && !opts.RemoveUnusedAnimationEvents && !opts.RemoveAllAnimationEvents)
            {
                return Fail("Select at least one option to clean");
            }

            int animEventsRemoved = 0;
            if (opts.RemoveUnusedAnimationEvents || opts.RemoveAllAnimationEvents)
                animEventsRemoved = RemoveAnimationEvents(controller, opts.RemoveUnusedAnimationEvents, opts.RemoveAllAnimationEvents, opts.CopyAnimationFiles);

            bool removeUnusedParams = opts.RemoveUnusedParams;
            bool removeDeadCode = opts.RemoveDeadCode;
            bool keepGestureWeights = opts.KeepGestureWeights;

            if (!removeUnusedParams && !removeDeadCode)
            {
                return new Result { Success = true, Removed = 0, RemovedAnimationEvents = animEventsRemoved };
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

            if (copy == null)
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

            HashSet<long> sIds = null;
            HashSet<long> smIds = null;
            if (removeDeadCode)
            {
                GetDestIds(outputAsset, out sIds, out smIds);
            }

            bool hasBrokenRef = false;
            if (removeDeadCode)
            {
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(outputAsset);
                foreach (var obj in subAssets)
                {
                    if (obj is AnimatorTransitionBase tr)
                    {
                        var so = new SerializedObject(tr);
                        // dstState is spooky
                        // before was going too deep and it exploded
                        var dstStateProp = so.FindProperty("m_DstState");
                        if (dstStateProp != null && dstStateProp.objectReferenceValue == null)
                        {
                            var dstStateFileId = GetInternalFileId(dstStateProp);
                            if (dstStateFileId != 0 && !sIds.Contains(dstStateFileId))
                            {
                                hasBrokenRef = true;
                                break;
                            }
                        }

                        var dstSmProp = so.FindProperty("m_DstStateMachine");
                        if (dstSmProp != null && dstSmProp.objectReferenceValue == null)
                        {
                            var dstSmFileId = GetInternalFileId(dstSmProp);
                            if (dstSmFileId != 0 && !smIds.Contains(dstSmFileId))
                            {
                                hasBrokenRef = true;
                                break;
                            }
                        }
                    }
                }
            }

            bool controllerChanged = removedNames.Count > 0 || ghostParamList.Count > 0 || hasBrokenRef;

            if (!controllerChanged)
            {
                AssetDatabase.DeleteAsset(outputAsset);
                return new Result
                {
                    Success = true,
                    Removed = 0,
                    RemovedAnimationEvents = animEventsRemoved
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
                        CleanGhostRefsFromStateMachine(l.stateMachine, ghostParams, sIds, smIds);
                    }
                }

                CleanAllSubAssets(outputAsset, ghostParams, sIds, smIds);
            }
            else if (removeDeadCode && hasBrokenRef)
            {
                foreach (var l in copy.layers)
                {
                    if (l.stateMachine != null)
                    {
                        CleanGhostRefsFromStateMachine(l.stateMachine, new HashSet<string>(), sIds, smIds);
                    }
                }

                CleanAllSubAssets(outputAsset, new HashSet<string>(), sIds, smIds);
            }

            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(outputAsset, ImportAssetOptions.ForceUpdate);

            return new Result
            {
                Success = true,
                Removed = removedNames.Count,
                RemovedNamed = removedNames,
                GhostParams = ghostParamList,
                RemovedAnimationEvents = animEventsRemoved
            };
        }
    }
}