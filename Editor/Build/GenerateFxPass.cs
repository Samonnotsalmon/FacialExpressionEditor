using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情レイヤーとパーツレイヤーを生成し、元FXのジェスチャーレイヤー・パーツレイヤーと置き換える。
    /// </summary>
    internal class GenerateFxPass : Pass<GenerateFxPass>
    {
        private const string LayerPrefix = "[FacialExpressionEditor] ";

        private const string GestureLeft = "GestureLeft";
        private const string GestureRight = "GestureRight";
        private const string GestureLeftWeight = "GestureLeftWeight";
        private const string GestureRightWeight = "GestureRightWeight";

        public override string DisplayName => "表情レイヤーを生成";

        protected override void Execute(BuildContext context)
        {
            var variant = context.AvatarRootObject.GetComponentInChildren<ExpressionVariant>(true);
            if (variant == null) return;

            var set = variant.expressionSet;
            if (set != null) Generate(context, set, variant);

            Object.DestroyImmediate(variant);
        }

        private static void Generate(BuildContext context, ExpressionSet set, ExpressionVariant variant)
        {
            var controllerContext = context.Extension<AnimatorServicesContext>().ControllerContext;
            if (!controllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null)
            {
                Debug.LogWarning("[FacialExpressionEditor] FXレイヤーが見つからないため、表情を生成しませんでした。");
                return;
            }

            var plan = BuildPlan.Create(set);
            var writeDefaults = DetectWriteDefaults(fx);
            var builder = new ExpressionClipBuilder(variant, context.AvatarRootObject);
            var cloneContext = controllerContext.CloneContext;

            var expressionLayer = BuildExpressionLayer(cloneContext, set, plan, builder, writeDefaults);
            var partLayers = BuildPartLayers(cloneContext, set, plan, builder, writeDefaults);

            EnsureParameter(fx, GestureLeft, AnimatorControllerParameterType.Int);
            EnsureParameter(fx, GestureRight, AnimatorControllerParameterType.Int);
            EnsureParameter(fx, GestureLeftWeight, AnimatorControllerParameterType.Float);
            EnsureParameter(fx, GestureRightWeight, AnimatorControllerParameterType.Float);
            if (plan.UsesSetParameter) EnsureParameter(fx, BuildPlan.SetParameter, AnimatorControllerParameterType.Int);
            if (plan.UsesFixedParameter) EnsureParameter(fx, BuildPlan.FixedParameter, AnimatorControllerParameterType.Int);
            foreach (var entry in plan.Parts)
            {
                EnsureParameter(fx, entry.Parameter,
                    entry.IsGrouped ? AnimatorControllerParameterType.Int : AnimatorControllerParameterType.Bool);
            }

            ReplaceOriginalLayers(fx, set, expressionLayer, partLayers);
        }

        private static VirtualLayer BuildExpressionLayer(CloneContext cloneContext, ExpressionSet set, BuildPlan plan,
            ExpressionClipBuilder builder, bool writeDefaults)
        {
            var layer = VirtualLayer.Create(cloneContext, LayerPrefix + "Expression");
            var stateMachine = layer.StateMachine;
            var clips = new List<VirtualClip>();

            var neutralClip = VirtualClip.Create("Neutral");
            clips.Add(neutralClip);
            var neutral = stateMachine.AddState("Neutral", neutralClip, new Vector3(300, 0, 0));
            neutral.WriteDefaultValues = writeDefaults;
            stateMachine.DefaultState = neutral;

            // 同じ表情でも、Fistの握り具合で動かすかどうかで別のステートにする。
            var states = new Dictionary<(string id, string timeParameter), VirtualState>();
            VirtualState StateFor(Expression expression, string timeParameter)
            {
                if (states.TryGetValue((expression.id, timeParameter), out var state)) return state;

                var name = timeParameter == null ? expression.name : $"{expression.name} ({timeParameter})";
                var clip = builder.Build(expression);
                clips.Add(clip);
                state = stateMachine.AddState(name, clip, new Vector3(600, 60 * states.Count, 0));
                state.WriteDefaultValues = writeDefaults;
                state.TimeParameter = timeParameter;
                states[(expression.id, timeParameter)] = state;
                return state;
            }

            var transitions = new List<VirtualStateTransition>();

            // メニューで固定した表情は、ジェスチャーより優先する。
            foreach (var pair in plan.FixedValues)
            {
                var expression = set.FindExpression(pair.Key);
                transitions.Add(Transition(StateFor(expression, null), set.GetTransitionDuration(expression),
                    Condition(BuildPlan.FixedParameter, AnimatorConditionMode.Equals, pair.Value)));
            }

            for (var setIndex = 0; setIndex < plan.GestureSets.Count; setIndex++)
            {
                var mapping = plan.GestureSets[setIndex].mapping;
                for (var l = 0; l < GestureMapping.GestureCount; l++)
                {
                    for (var r = 0; r < GestureMapping.GestureCount; r++)
                    {
                        var leftGesture = (HandGesture)l;
                        var rightGesture = (HandGesture)r;
                        var expression = set.FindExpression(mapping.Resolve(leftGesture, rightGesture, out var source));

                        VirtualState target;
                        if (expression == null)
                        {
                            target = neutral;
                        }
                        else
                        {
                            var sourceGesture = source == Hand.Left ? leftGesture : rightGesture;
                            var timeParameter = sourceGesture == HandGesture.Fist
                                ? (source == Hand.Left ? GestureLeftWeight : GestureRightWeight)
                                : null;
                            target = StateFor(expression, timeParameter);
                        }

                        var conditions = new List<AnimatorCondition>();
                        if (plan.UsesFixedParameter) conditions.Add(Condition(BuildPlan.FixedParameter, AnimatorConditionMode.Equals, 0));
                        if (plan.UsesSetParameter) conditions.Add(Condition(BuildPlan.SetParameter, AnimatorConditionMode.Equals, setIndex));
                        conditions.Add(Condition(GestureLeft, AnimatorConditionMode.Equals, l));
                        conditions.Add(Condition(GestureRight, AnimatorConditionMode.Equals, r));

                        transitions.Add(Transition(target, set.GetTransitionDuration(expression), conditions.ToArray()));
                    }
                }
            }

            stateMachine.AnyStateTransitions = transitions.ToImmutableList();

            if (!writeDefaults) builder.FillMissingWithDefaults(clips);
            return layer;
        }

        private static List<VirtualLayer> BuildPartLayers(CloneContext cloneContext, ExpressionSet set, BuildPlan plan,
            ExpressionClipBuilder builder, bool writeDefaults)
        {
            var layers = new List<VirtualLayer>();
            var duration = set.defaultTransitionDuration;

            foreach (var entry in plan.Parts.Where(p => !p.IsGrouped))
            {
                var layer = VirtualLayer.Create(cloneContext, LayerPrefix + "Part " + entry.Part.name);
                var stateMachine = layer.StateMachine;
                var offClip = VirtualClip.Create("Off");
                var onClip = builder.BuildPart(entry.Part);

                var off = stateMachine.AddState("Off", offClip, new Vector3(300, 0, 0));
                var on = stateMachine.AddState("On", onClip, new Vector3(300, 80, 0));
                off.WriteDefaultValues = writeDefaults;
                on.WriteDefaultValues = writeDefaults;
                stateMachine.DefaultState = off;

                off.Transitions = ImmutableList.Create(Transition(on, duration,
                    Condition(entry.Parameter, AnimatorConditionMode.If, 0)));
                on.Transitions = ImmutableList.Create(Transition(off, duration,
                    Condition(entry.Parameter, AnimatorConditionMode.IfNot, 0)));

                if (!writeDefaults) builder.FillMissingWithDefaults(new[] { offClip, onClip });
                layers.Add(layer);
            }

            foreach (var group in plan.PartGroups)
            {
                var layer = VirtualLayer.Create(cloneContext, LayerPrefix + "Parts " + group.Name);
                var stateMachine = layer.StateMachine;
                var clips = new List<VirtualClip>();

                var offClip = VirtualClip.Create("Off");
                clips.Add(offClip);
                var off = stateMachine.AddState("Off", offClip, new Vector3(300, 0, 0));
                off.WriteDefaultValues = writeDefaults;
                stateMachine.DefaultState = off;

                var transitions = new List<VirtualStateTransition>
                {
                    Transition(off, duration, Condition(group.Parameter, AnimatorConditionMode.Equals, 0)),
                };

                foreach (var entry in group.Parts)
                {
                    var clip = builder.BuildPart(entry.Part);
                    clips.Add(clip);
                    var state = stateMachine.AddState(entry.Part.name, clip, new Vector3(600, 60 * entry.Value, 0));
                    state.WriteDefaultValues = writeDefaults;
                    transitions.Add(Transition(state, duration,
                        Condition(group.Parameter, AnimatorConditionMode.Equals, entry.Value)));
                }

                stateMachine.AnyStateTransitions = transitions.ToImmutableList();

                if (!writeDefaults) builder.FillMissingWithDefaults(clips);
                layers.Add(layer);
            }

            return layers;
        }

        private static VirtualStateTransition Transition(VirtualState target, float duration, params AnimatorCondition[] conditions)
        {
            var transition = VirtualStateTransition.Create();
            transition.SetDestination(target);
            transition.Conditions = conditions.ToImmutableList();
            transition.CanTransitionToSelf = false;
            transition.ExitTime = null;
            transition.HasFixedDuration = true;
            transition.Duration = duration;
            return transition;
        }

        private static AnimatorCondition Condition(string parameter, AnimatorConditionMode mode, float threshold)
        {
            return new AnimatorCondition { parameter = parameter, mode = mode, threshold = threshold };
        }

        /// <summary>
        /// 元FXのステートの多数決で、Write Defaultsのオン・オフを決める。
        /// </summary>
        private static bool DetectWriteDefaults(VirtualAnimatorController fx)
        {
            var states = fx.Layers
                .Where(l => l.IsOriginalLayer)
                .SelectMany(l => l.AllReachableNodes().OfType<VirtualState>())
                .ToList();
            if (states.Count == 0) return true;

            var on = states.Count(s => s.WriteDefaultValues);
            return on * 2 >= states.Count;
        }

        private static void EnsureParameter(VirtualAnimatorController fx, string name, AnimatorControllerParameterType type)
        {
            if (fx.Parameters.ContainsKey(name)) return;
            fx.SetParameter(name, new AnimatorControllerParameter { name = name, type = type });
        }

        /// <summary>
        /// 元FXのジェスチャーレイヤー・パーツレイヤーを取り除き、それぞれ最初にあった位置に生成レイヤーを入れる。
        /// 他のレイヤー（口モーフキャンセラーなど）との重なり順は元のまま保たれる。
        /// </summary>
        private static void ReplaceOriginalLayers(VirtualAnimatorController fx, ExpressionSet set,
            VirtualLayer expressionLayer, List<VirtualLayer> partLayers)
        {
            var result = new List<VirtualLayer>();
            var expressionInserted = false;
            var partsInserted = false;

            foreach (var layer in fx.Layers)
            {
                if (layer.IsOriginalLayer && set.originalGestureLayers.Contains(layer.Name))
                {
                    if (!expressionInserted) result.Add(expressionLayer);
                    expressionInserted = true;
                    continue;
                }

                if (layer.IsOriginalLayer && set.originalPartLayers.Contains(layer.Name))
                {
                    if (!partsInserted) result.AddRange(partLayers);
                    partsInserted = true;
                    continue;
                }

                result.Add(layer);
            }

            if (!expressionInserted) result.Add(expressionLayer);
            if (!partsInserted) result.InsertRange(result.IndexOf(expressionLayer) + 1, partLayers);

            fx.Layers = result;
        }
    }
}
