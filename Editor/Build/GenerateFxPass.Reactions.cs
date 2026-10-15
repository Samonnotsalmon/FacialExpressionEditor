using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class GenerateFxPass
    {
        // Only motions are replaced. State identity, transitions, behaviour and layer order are retained.
        internal static void ApplyReactionClips(VirtualAnimatorController fx, ExpressionSet set, ExpressionClipBuilder builder)
        {
            foreach (var group in set.reactionClips.GroupBy(c => c.layerName))
            {
                if (set.originalGestureLayers.Contains(group.Key) || set.originalPartLayers.Contains(group.Key) ||
                    set.originalLayerSettings.Any(s => s.layerName == group.Key && s.mode == OriginalLayerMode.Disable)) continue;
                var layers = fx.Layers.Where(l => l.Name == group.Key).ToList();
                if (layers.Count != 1) throw new InvalidOperationException("反応の差し替え先レイヤーが見つからないか重複しています：" + group.Key);
                if (layers[0].SyncedLayerIndex >= 0 || fx.Layers.Any(l => l != layers[0] && l.StateMachine == layers[0].StateMachine))
                    throw new InvalidOperationException("同期・共有ステートマシンの反応は個別差し替えできません：" + group.Key);
                var mappings = group.ToList();
                var used = new HashSet<ReactionClip>();
                var cache = new Dictionary<VirtualMotion, VirtualMotion>();
                VirtualMotion Replace(VirtualMotion motion)
                {
                    if (motion == null) return null;
                    if (cache.TryGetValue(motion, out var cached)) return cached;
                    if (motion is VirtualClip clip)
                    {
                        var original = OriginalObjectProperty.GetValue(clip) as AnimationClip;
                        var mapping = mappings.Find(m => m.source == original);
                        if (mapping == null) return motion;
                        var expression = set.FindExpression(mapping.expressionId);
                        if (expression?.clip == null) throw new InvalidOperationException("反応の差し替えクリップがありません：" + group.Key);
                        used.Add(mapping);
                        return cache[motion] = CopyReactionClip(builder.Build(expression));
                    }
                    if (motion is VirtualBlendTree tree)
                    {
                        var copy = VirtualBlendTree.Create(tree.Name);
                        copy.BlendType = tree.BlendType; copy.BlendParameter = tree.BlendParameter; copy.BlendParameterY = tree.BlendParameterY;
                        copy.NormalizedBlendValues = tree.NormalizedBlendValues;
                        cache[motion] = copy;
                        copy.Children = tree.Children.Select(c => new VirtualBlendTree.VirtualChildMotion
                        {
                            Motion = Replace(c.Motion), Threshold = c.Threshold, Position = c.Position, TimeScale = c.TimeScale,
                            CycleOffset = c.CycleOffset, DirectBlendParameter = c.DirectBlendParameter, Mirror = c.Mirror,
                        }).ToImmutableList();
                        return copy;
                    }
                    return motion;
                }
                foreach (var state in layers[0].AllReachableNodes().OfType<VirtualState>()) state.Motion = Replace(state.Motion);
                if (used.Count != mappings.Count) throw new InvalidOperationException("元FXの反応クリップが変更されています。「反応」タブで差し替えを元に戻して再設定してください：" + group.Key);
            }
        }

        internal static void GateGestureOnlyLinkedMotion(VirtualAnimatorController fx, CloneContext context, FaceControlPlan face)
        {
            var paths = new HashSet<string>(fx.Layers.Where(l => face.ReactionLayers.Contains(l.Name))
                .SelectMany(l => l.AllReachableNodes().OfType<VirtualState>()).SelectMany(s => ClipsOf(s.Motion))
                .SelectMany(c => c.GetFloatCurveBindings()).Where(b => b.type == typeof(Transform)).Select(b => b.path));
            if (paths.Count == 0) return;
            var gates = new List<VirtualLayer>();
            foreach (var layer in fx.Layers.Where(l => !l.Name.StartsWith(LayerPrefix) && !face.ReactionLayers.Contains(l.Name)).ToList())
            {
                var states = layer.AllReachableNodes().OfType<VirtualState>().ToList();
                var conditions = layer.AllReachableNodes().OfType<VirtualTransitionBase>().SelectMany(t => t.Conditions).ToList();
                if (!conditions.Any(c => c.parameter == GestureLeft || c.parameter == GestureRight)) continue;
                var clips = states.SelectMany(s => ClipsOf(s.Motion)).Distinct().ToList();
                var bindings = clips.SelectMany(c => c.GetFloatCurveBindings()).ToList();
                // Only the separate gesture-driven animation of already linked bones is suppressed.
                // A mixed face/body or wardrobe layer is never disabled by this inference.
                if (bindings.Count == 0 || bindings.Any(b => b.type != typeof(Transform) || !paths.Contains(b.path)) || clips.Any(c => c.GetObjectCurveBindings().Any())) continue;
                var hasOwnWeightControl = fx.Layers.SelectMany(l => l.AllReachableNodes().OfType<VirtualState>()).SelectMany(s => s.Behaviours)
                    .OfType<VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl>().Any(c => c.layer == layer.VirtualLayerIndex &&
                        c.playable == VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.FX);
                if (hasOwnWeightControl)
                {
                    Debug.LogWarning("[FacialExpressionEditor] 固定中の連動ジェスチャー停止：独自のレイヤー重み制御を持つため、元の制御を維持します：" + layer.Name);
                    continue;
                }
                var gate = VirtualLayer.Create(context, LayerPrefix + "Fixed linked motion " + layer.Name);
                var normal = gate.StateMachine.AddState("Gesture", VirtualClip.Create("Gesture"), Vector3.zero);
                var fixedState = gate.StateMachine.AddState("Fixed", VirtualClip.Create("Fixed"), new Vector3(300, 0, 0));
                normal.WriteDefaultValues = fixedState.WriteDefaultValues = true;
                gate.StateMachine.DefaultState = normal;
                normal.Transitions = ImmutableList.Create(Transition(fixedState, 0, Condition(BuildPlan.EmoteParameter, AnimatorConditionMode.Greater, 0)));
                fixedState.Transitions = ImmutableList.Create(Transition(normal, 0, Condition(BuildPlan.EmoteParameter, AnimatorConditionMode.Equals, 0)));
                SetPartLayerWeight(normal, layer, layer.DefaultWeight);
                SetPartLayerWeight(fixedState, layer, 0);
                gates.Add(gate);
            }
            fx.Layers = fx.Layers.Concat(gates).ToList();
        }

        private static VirtualClip CopyReactionClip(VirtualClip source)
        {
            var copy = VirtualClip.Create(source.Name); copy.Settings = source.Settings;
            foreach (var b in source.GetFloatCurveBindings()) copy.SetFloatCurve(b, source.GetFloatCurve(b));
            foreach (var b in source.GetObjectCurveBindings()) copy.SetObjectCurve(b, source.GetObjectCurve(b));
            return copy;
        }

        // A fixed-face overlay leaves the original reaction graph running, including motion in the same clip.
        // It owns only facial renderer properties. No fallback curves are written into saved editable clips.
        internal static VirtualLayer BuildReactionFixedFace(VirtualAnimatorController fx, CloneContext context,
            ExpressionSet set, BuildPlan plan, FaceControlPlan face, ExpressionClipBuilder builder, GameObject root)
        {
            var sources = fx.Layers.Where(l => face.ReactionLayers.Contains(l.Name)).ToList();
            if (sources.Count == 0) return null;
            var facialPaths = set.faceMeshPaths.Count > 0 ? new HashSet<string>(set.faceMeshPaths) : face.FacePaths;
            bool IsFace(EditorCurveBinding b) => facialPaths.Contains(b.path) && typeof(Renderer).IsAssignableFrom(b.type) &&
                !LipSyncProtection.IsProtected(b, root, set) && !set.reactionFixedPassthroughProperties.Contains(ExpressionClipBuilder.Key(b));
            var sourceClips = sources.SelectMany(l => l.AllReachableNodes().OfType<VirtualState>()).SelectMany(s => ClipsOf(s.Motion)).Distinct().ToList();
            var floats = sourceClips.SelectMany(c => c.GetFloatCurveBindings()).Where(IsFace).Distinct().ToList();
            var objects = sourceClips.SelectMany(c => c.GetObjectCurveBindings()).Where(IsFace).Distinct().ToList();
            if (floats.Count + objects.Count == 0) return null;
            var layer = VirtualLayer.Create(context, LayerPrefix + "Fixed face over reactions");
            layer.DefaultWeight = 0;
            var idle = layer.StateMachine.AddState("Not fixed", VirtualClip.Create("Not fixed"), Vector3.zero);
            idle.WriteDefaultValues = true; layer.StateMachine.DefaultState = idle; SetPartLayerWeight(idle, layer, 0);
            var transitions = new List<VirtualStateTransition> { Transition(idle, 0, Condition(BuildPlan.EmoteParameter, AnimatorConditionMode.Equals, 0)) };
            var baseFace = builder.BuildBaseFace();
            var materialScope = VirtualClip.Create("Fixed face material scope");
            foreach (var b in floats.Where(b => b.propertyName.StartsWith("material.")))
                materialScope.SetFloatCurve(b, sourceClips.Select(c => c.GetFloatCurve(b)).First(c => c != null));
            VirtualClip FixedClip(VirtualClip source)
            {
                var copy = CopyReactionClip(source);
                foreach (var b in copy.GetFloatCurveBindings().Where(b => !IsFace(b)).ToList()) copy.SetFloatCurve(b, null);
                foreach (var b in copy.GetObjectCurveBindings().Where(b => !IsFace(b)).ToList()) copy.SetObjectCurve(b, null);
                foreach (var b in floats.Where(b => copy.GetFloatCurve(b) == null))
                {
                    var curve = baseFace.GetFloatCurve(b);
                    if (curve != null) copy.SetFloatCurve(b, curve);
                    else if (!b.propertyName.StartsWith("material.") && AnimationUtility.GetFloatValue(root, b, out var value))
                        copy.SetFloatCurve(b, AnimationCurve.Constant(0, 1, value));
                }
                foreach (var b in objects.Where(b => copy.GetObjectCurve(b) == null))
                    if (AnimationUtility.GetObjectReferenceValue(root, b, out var value)) copy.SetObjectCurve(b, new[] { new ObjectReferenceKeyframe { time = 0, value = value } });
                // Reuse the original FX material defaults, without letting reaction "on" values become defaults.
                builder.FillMissingMaterialDefaults(new[] { copy, materialScope });
                return copy;
            }
            void Add(int value, Expression expression, VirtualClip source)
            {
                var state = layer.StateMachine.AddState("Fixed " + value, FixedClip(source), new Vector3(300, value * 70, 0));
                state.WriteDefaultValues = true;
                SetPartLayerWeight(state, layer, 1);
                transitions.Add(Transition(state, set.GetTransitionDuration(expression), Condition(BuildPlan.EmoteParameter, AnimatorConditionMode.Equals, value)));
            }
            Add(plan.BaseFaceValue, null, baseFace);
            foreach (var pair in plan.EmoteValues)
            {
                var expression = set.FindExpression(pair.Key);
                var effect = set.GetFixedSwitchEffect(expression);
                if (!expression.freezeAnimation && PreviewClips.IsTimeVarying(expression.clip)) effect = null;
                Add(pair.Value, expression, effect != null ? builder.BuildSwitch(expression, set.FindExpression(effect.betweenExpressionId), effect) : builder.Build(expression));
            }
            layer.StateMachine.AnyStateTransitions = transitions.ToImmutableList();
            return layer;
        }
    }
}
