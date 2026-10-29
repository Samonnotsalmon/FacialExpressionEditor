using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Samon.FacialExpressionEditor.Editor
{
    // Only direct sensor-driven facial layers are editable. Do not follow their output drivers into face services or bones.
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
            reads.Remove(""); reads.Remove(null);
            return reads;
        }

        internal static List<Candidate> Scan(VRCAvatarDescriptor descriptor, ExpressionSet set, FaceVariant variant)
        {
            var result = new List<Candidate>();
            var fx = FxImporter.GetFx(descriptor);
            if (fx == null) return result;
            var layers = fx.layers;
            var sensors = OriginalFxAnalysis.ContactParameters(descriptor);
            var reads = layers.Select(Reads).ToList();
            var excluded = new HashSet<string>(set.originalGestureLayers.Concat(set.originalPartLayers));
            var facePaths = OriginalFxAnalysis.FacePathsOf(descriptor, set, variant);
            facePaths.UnionWith(set.faceMeshPaths);
            bool Face(AnimationClip c) => AnimationUtility.GetCurveBindings(c).Any(b => facePaths.Contains(b.path) && typeof(Renderer).IsAssignableFrom(b.type));
            for (var i = layers.Length - 1; i >= 0; i--)
            {
                var layer = layers[i];
                if (excluded.Contains(layer.name)) continue;
                var inputs = sensors.Where(p => reads[i].Contains(p.parameter)).Select(p => p.parameter).ToList();
                if (inputs.Count == 0) continue;
                var clips = FxImporter.AllStates(layer.stateMachine).SelectMany(s => Clips(s.motion)).Distinct().ToList();
                if (!clips.Any(Face)) continue;
                result.Add(new Candidate { Name = layer.name, Layers = new List<string> { layer.name }, Clips = clips, Parameters = inputs });
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
                if (binding.type == typeof(Transform) || (string.IsNullOrEmpty(binding.path) && binding.type == typeof(Animator))) continue;
                var t = string.IsNullOrEmpty(binding.path) ? root.transform : root.transform.Find(binding.path);
                if (t == null) continue;
                if (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape."))
                { if (!set.faceMeshPaths.Contains(binding.path) && !set.linkedObjectPaths.Contains(binding.path)) set.linkedObjectPaths.Add(binding.path); }
                else if (!set.linkedObjectPaths.Contains(binding.path)) set.linkedObjectPaths.Add(binding.path);
            }
        }
    }
}
