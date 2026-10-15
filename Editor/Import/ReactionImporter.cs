using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace Samon.FacialExpressionEditor.Editor
{
    // Read the existing graph, including parameter-driver dependencies. Never infer a reaction from clip names.
    internal static class ReactionImporter
    {
        internal sealed class Candidate
        {
            public string Name;
            public List<string> Layers = new List<string>();
            public List<AnimationClip> Clips = new List<AnimationClip>();
            public List<string> Parameters = new List<string>();
        }

        internal static IEnumerable<AnimationClip> Clips(Motion motion)
        {
            if (motion is AnimationClip clip) yield return clip;
            if (motion is BlendTree tree)
                foreach (var c in tree.children.SelectMany(c => Clips(c.motion))) yield return c;
        }

        private static IEnumerable<AnimatorStateMachine> Machines(AnimatorStateMachine machine)
        {
            yield return machine;
            foreach (var child in machine.stateMachines.SelectMany(s => Machines(s.stateMachine))) yield return child;
        }

        private static HashSet<string> Reads(AnimatorControllerLayer layer)
        {
            var reads = new HashSet<string>(Machines(layer.stateMachine).SelectMany(m =>
                m.entryTransitions.Cast<AnimatorTransitionBase>().Concat(m.anyStateTransitions)
                .Concat(m.states.SelectMany(s => s.state.transitions))
                .Concat(m.stateMachines.SelectMany(s => m.GetStateMachineTransitions(s.stateMachine))))
                .SelectMany(t => t.conditions).Select(c => c.parameter));
            foreach (var state in FxImporter.AllStates(layer.stateMachine))
            {
                if (state.timeParameterActive) reads.Add(state.timeParameter);
                if (state.speedParameterActive) reads.Add(state.speedParameter);
                if (state.cycleOffsetParameterActive) reads.Add(state.cycleOffsetParameter);
                void Tree(Motion m)
                {
                    if (!(m is BlendTree t)) return;
                    reads.Add(t.blendParameter); reads.Add(t.blendParameterY);
                    foreach (var child in t.children) { reads.Add(child.directBlendParameter); Tree(child.motion); }
                }
                Tree(state.motion);
            }
            foreach (var driver in Behaviours(layer).OfType<VRCAvatarParameterDriver>())
                foreach (var p in driver.parameters.Where(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy)) reads.Add(p.source);
            reads.Remove(""); reads.Remove(null);
            return reads;
        }

        private static IEnumerable<StateMachineBehaviour> Behaviours(AnimatorControllerLayer layer) =>
            Machines(layer.stateMachine).SelectMany(m => m.behaviours.Concat(m.states.SelectMany(s => s.state.behaviours)));

        internal static List<Candidate> Scan(VRCAvatarDescriptor descriptor, ExpressionSet set, FaceVariant variant)
        {
            var result = new List<Candidate>();
            var fx = FxImporter.GetFx(descriptor);
            if (fx == null) return result;
            var layers = fx.layers;
            var sensors = OriginalFxAnalysis.ContactParameters(descriptor);
            var reads = layers.Select(Reads).ToList();
            var writes = layers.Select(l => new HashSet<string>(Behaviours(l).OfType<VRCAvatarParameterDriver>()
                .SelectMany(d => d.parameters).Select(p => p.name))).ToList();
            var facePaths = OriginalFxAnalysis.FacePathsOf(descriptor, set, variant);
            facePaths.UnionWith(set.faceMeshPaths);
            bool Face(AnimationClip c) => AnimationUtility.GetCurveBindings(c).Any(b => facePaths.Contains(b.path) && typeof(Renderer).IsAssignableFrom(b.type));
            var groups = new List<HashSet<int>>();
            foreach (var sensor in sensors)
            {
                var parameters = new HashSet<string> { sensor.parameter };
                var found = new HashSet<int>();
                bool changed;
                do
                {
                    changed = false;
                    for (var i = 0; i < layers.Length; i++)
                        if (reads[i].Overlaps(parameters) && found.Add(i)) { parameters.UnionWith(writes[i]); changed = true; }
                } while (changed);
                if (found.Count == 0) continue;
                // Shared state graphs cannot be split without changing their priority/driver semantics.
                foreach (var old in groups.Where(g => g.Overlaps(found)).ToList()) { found.UnionWith(old); groups.Remove(old); }
                groups.Add(found);
            }
            foreach (var group in groups.OrderByDescending(g => g.Max()))
            {
                var clips = group.SelectMany(i => FxImporter.AllStates(layers[i].stateMachine)).SelectMany(s => Clips(s.motion)).Distinct().ToList();
                if (!clips.Any(Face)) continue;
                var candidate = new Candidate { Name = layers[group.Max()].name, Layers = group.OrderBy(i => i).Select(i => layers[i].name).ToList(), Clips = clips };
                candidate.Parameters = sensors.Where(p => group.Any(i => reads[i].Contains(p.parameter))).Select(p => p.parameter).ToList();
                result.Add(candidate);
            }
            return result;
        }

        internal static Expression EditClip(ExpressionSet set, string layer, AnimationClip source, AnimationClip replacement, GameObject root)
        {
            var entry = set.reactionClips.Find(c => c.layerName == layer && c.source == source);
            var existing = entry != null ? set.FindExpression(entry.expressionId) : null;
            if (existing != null && replacement == null) return existing;
            var from = replacement != null ? replacement : source;
            if (from == null) return null;
            var folder = AssetPathUtility.EnsureFolder(AssetPathUtility.FolderOf(set), "Expressions");
            var clip = Object.Instantiate(from); clip.name = from.name;
            LipSyncProtection.Remove(clip, root, set);
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + AssetPathUtility.SafeFileName(from.name) + ".anim"));
            Undo.RegisterCreatedObjectUndo(clip, "反応クリップを複製");
            Undo.RecordObject(set, "反応クリップを差し替え");
            var expression = new Expression { name = clip.name, clip = clip, originalClip = from, baseFaceBaked = true };
            ExpressionSetUtility.MatchExpressionNameToFile(set, expression);
            set.expressions.Add(expression);
            if (entry == null) { entry = new ReactionClip { layerName = layer, source = source }; set.reactionClips.Add(entry); }
            entry.expressionId = expression.id;
            AddTargets(set, root, clip);
            EditorUtility.SetDirty(set); AssetDatabase.SaveAssets();
            return expression;
        }

        internal static void AddTargets(ExpressionSet set, GameObject root, AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
            {
                if (string.IsNullOrEmpty(binding.path) && binding.type == typeof(Animator)) continue;
                var t = string.IsNullOrEmpty(binding.path) ? root.transform : root.transform.Find(binding.path);
                if (t == null) continue;
                if (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape."))
                { if (!set.faceMeshPaths.Contains(binding.path) && !set.linkedObjectPaths.Contains(binding.path)) set.linkedObjectPaths.Add(binding.path); }
                else if (!set.linkedObjectPaths.Contains(binding.path)) set.linkedObjectPaths.Add(binding.path);
            }
        }
    }
}
