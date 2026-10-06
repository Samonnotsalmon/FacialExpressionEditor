using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

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
            var avatar = context.AvatarRootObject.GetComponentInChildren<FacialExpressionAvatar>(true);
            if (avatar == null) return;

            var set = avatar.expressionSet;
            if (set != null) Generate(context, set, avatar.faceVariant);

            Object.DestroyImmediate(avatar);
        }

        private static void Generate(BuildContext context, ExpressionSet set, FaceVariant variant)
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

            var expressionLayers = new List<VirtualLayer> { BuildExpressionLayer(cloneContext, set, plan, builder, writeDefaults) };
            if (plan.UsesModeParameter && plan.UsesEmoteParameter) expressionLayers.Add(BuildEmoteReleaseLayer(cloneContext, plan, writeDefaults));
            var partLayers = BuildPartLayers(cloneContext, set, plan, builder, writeDefaults);

            EnsureParameter(fx, GestureLeft, AnimatorControllerParameterType.Int);
            EnsureParameter(fx, GestureRight, AnimatorControllerParameterType.Int);
            EnsureParameter(fx, GestureLeftWeight, AnimatorControllerParameterType.Float);
            EnsureParameter(fx, GestureRightWeight, AnimatorControllerParameterType.Float);
            if (plan.UsesModeParameter) EnsureParameter(fx, BuildPlan.ModeParameter, AnimatorControllerParameterType.Int);
            if (plan.UsesEmoteParameter) EnsureParameter(fx, BuildPlan.EmoteParameter, AnimatorControllerParameterType.Int);
            foreach (var entry in plan.Parts)
            {
                EnsureParameter(fx, entry.Parameter,
                    entry.IsGrouped ? AnimatorControllerParameterType.Int : AnimatorControllerParameterType.Bool);
            }

            ReplaceOriginalLayers(fx, set, expressionLayers, partLayers);
        }

        /// <summary>
        /// 表情レイヤーを作る。人が見て追えるように、ルートに無表情、サブステートマシン「ジェスチャー」と「固定」に
        /// 表情のステートを置き、遷移には「どの条件でどの表情になるか」が分かる名前を付ける。
        /// </summary>
        private static VirtualLayer BuildExpressionLayer(CloneContext cloneContext, ExpressionSet set, BuildPlan plan,
            ExpressionClipBuilder builder, bool writeDefaults)
        {
            var layer = VirtualLayer.Create(cloneContext, LayerPrefix + "Expression");
            var root = layer.StateMachine;
            var clips = new List<VirtualClip>();

            var neutralClip = VirtualClip.Create("無表情");
            clips.Add(neutralClip);
            var neutral = root.AddState("無表情", neutralClip, new Vector3(300, 120, 0));
            neutral.WriteDefaultValues = writeDefaults;
            root.DefaultState = neutral;

            var gestureMachine = VirtualStateMachine.Create(cloneContext, "ジェスチャー");
            var fixedMachine = VirtualStateMachine.Create(cloneContext, "固定");
            root.StateMachines = root.StateMachines
                .Add(new VirtualStateMachine.VirtualChildStateMachine { StateMachine = gestureMachine, Position = new Vector3(600, 60, 0) })
                .Add(new VirtualStateMachine.VirtualChildStateMachine { StateMachine = fixedMachine, Position = new Vector3(600, 180, 0) });

            VirtualState AddState(VirtualStateMachine machine, string name, VirtualClip clip, int index)
            {
                clips.Add(clip);
                var state = machine.AddState(name, clip, new Vector3(300 + index % 4 * 240, index / 4 * 70, 0));
                state.WriteDefaultValues = writeDefaults;
                return state;
            }

            // 同じ表情でも、Fistの握り具合で動かすかどうかで別のステートにする。
            var gestureStates = new Dictionary<(string id, string timeParameter), VirtualState>();
            VirtualState GestureState(Expression expression, string timeParameter)
            {
                if (gestureStates.TryGetValue((expression.id, timeParameter), out var state)) return state;

                var name = timeParameter == null ? expression.name : $"{expression.name}（{(timeParameter == GestureLeftWeight ? "左" : "右")}手の握り具合）";
                state = AddState(gestureMachine, name, builder.Build(expression), gestureStates.Count);
                state.TimeParameter = timeParameter;
                gestureStates[(expression.id, timeParameter)] = state;
                return state;
            }

            var transitions = new List<VirtualStateTransition>();

            // 固定用のステートは表情ごとに1つ（表情選択の値が違っても同じ表情なら共有する）。
            // 切り替え演出があればクリップの頭に挟む表情を入れる（ステートや遷移は増やさない）。
            var fixedStates = new Dictionary<string, VirtualState>();
            VirtualState FixedState(Expression expression)
            {
                if (fixedStates.TryGetValue(expression.id, out var state)) return state;

                var effect = set.GetFixedSwitchEffect(expression);
                var clip = effect != null
                    ? builder.BuildSwitch(expression, set.FindExpression(effect.betweenExpressionId), effect)
                    : builder.Build(expression);
                return fixedStates[expression.id] = AddState(fixedMachine, expression.name, clip, fixedStates.Count);
            }

            // 「表情選択」で選んだ表情（FEE/Emote）を優先し、選んでいない（0）ときだけモードの遷移が働く。
            // FaceEmoと同じく後から選んだほうが勝つように、モードを選び直すと「表情選択の解除」レイヤーが FEE/Emote を 0 に戻す。
            foreach (var pair in plan.EmoteValues)
            {
                var expression = set.FindExpression(pair.Key);
                var transition = Transition(FixedState(expression), set.GetTransitionDuration(expression),
                    Condition(BuildPlan.EmoteParameter, AnimatorConditionMode.Equals, pair.Value));
                transition.Name = $"表情選択 {pair.Value}: {expression.name}";
                transitions.Add(transition);
            }

            // メニューで選んだモード（FEE/Mode の値）ごとに遷移を作る。モードは1つだけ有効になるので条件は重ならない。
            foreach (var mode in plan.Modes)
            {
                var prefix = new List<AnimatorCondition>();
                if (plan.UsesEmoteParameter) prefix.Add(Condition(BuildPlan.EmoteParameter, AnimatorConditionMode.Equals, 0));
                if (plan.UsesModeParameter) prefix.Add(Condition(BuildPlan.ModeParameter, AnimatorConditionMode.Equals, mode.Value));

                var label = plan.UsesModeParameter ? $"モード {mode.Value} {mode.GestureSet.name}: " : "";
                AddGestureTransitions(set, mode.GestureSet.mapping, prefix, label, neutral, GestureState, transitions);
            }
            root.AnyStateTransitions = transitions.ToImmutableList();

            if (!writeDefaults) builder.FillMissingWithDefaults(clips);
            return layer;
        }

        /// <summary>
        /// 1つの表情セットのジェスチャー遷移を作る。結果は GestureMapping.Resolve と同じになるようにしつつ、
        /// 遷移の数を減らす（AnyState遷移は毎フレーム評価されるため）。
        /// - 優先する手のジェスチャーに表情があれば、反対の手は見ずに1本の遷移にする
        /// - 無ければ、反対の手のジェスチャーごとに遷移を作る
        /// - 組み合わせの上書きは個別の遷移にし、上の遷移からは「反対の手 != そのジェスチャー」で除く
        /// どの遷移も条件が重ならないので、並び順に関係なく1つだけが成り立つ。
        /// </summary>
        private static void AddGestureTransitions(ExpressionSet set, GestureMapping mapping, List<AnimatorCondition> prefix,
            string label, VirtualState neutral, System.Func<Expression, string, VirtualState> stateFor,
            List<VirtualStateTransition> transitions)
        {
            mapping.EnsureSize();
            var dominantIsLeft = mapping.dominantHand == Hand.Left;
            var dominantParameter = dominantIsLeft ? GestureLeft : GestureRight;
            var otherParameter = dominantIsLeft ? GestureRight : GestureLeft;
            var dominantWeight = dominantIsLeft ? GestureLeftWeight : GestureRightWeight;
            var otherWeight = dominantIsLeft ? GestureRightWeight : GestureLeftWeight;
            var dominantTable = dominantIsLeft ? mapping.left : mapping.right;
            var otherTable = dominantIsLeft ? mapping.right : mapping.left;
            var useDominantWeight = mapping.UsesFistWeight(mapping.dominantHand);
            var useOtherWeight = mapping.UsesFistWeight(dominantIsLeft ? Hand.Right : Hand.Left);
            var dominantName = dominantIsLeft ? "左手" : "右手";
            var otherName = dominantIsLeft ? "右手" : "左手";
            string GestureName(int g) => ClipUsage.GestureShortLabels[g];

            AnimatorCondition[] With(params AnimatorCondition[] conditions) => prefix.Concat(conditions).ToArray();

            void Add(VirtualState target, Expression expression, string name, AnimatorCondition[] conditions)
            {
                var transition = Transition(target, set.GetTransitionDuration(expression), conditions);
                transition.Name = $"{label}{name} → {(expression != null ? expression.name : "無表情")}";
                transitions.Add(transition);
            }

            // 同じ組み合わせが複数あれば、Resolve と同じく先にあるものを使う。
            var combos = mapping.combos
                .Where(c => set.FindExpression(c.expressionId) != null)
                .GroupBy(c => (c.left, c.right))
                .Select(g => g.First())
                .ToList();
            int DominantOf(GestureComboOverride c) => (int)(dominantIsLeft ? c.left : c.right);
            int OtherOf(GestureComboOverride c) => (int)(dominantIsLeft ? c.right : c.left);

            foreach (var combo in combos)
            {
                var expression = set.FindExpression(combo.expressionId);
                var timeParameter = DominantOf(combo) == (int)HandGesture.Fist && useDominantWeight ? dominantWeight : null;
                Add(stateFor(expression, timeParameter), expression,
                    $"左手 {GestureName((int)combo.left)} × 右手 {GestureName((int)combo.right)}",
                    With(Condition(GestureLeft, AnimatorConditionMode.Equals, (int)combo.left),
                        Condition(GestureRight, AnimatorConditionMode.Equals, (int)combo.right)));
            }

            for (var g = 0; g < GestureMapping.GestureCount; g++)
            {
                var comboOthers = combos.Where(c => DominantOf(c) == g).Select(OtherOf).ToList();
                var dominantExpression = set.FindExpression(dominantTable[g]);

                if (dominantExpression != null)
                {
                    var timeParameter = g == (int)HandGesture.Fist && useDominantWeight ? dominantWeight : null;
                    var conditions = new List<AnimatorCondition> { Condition(dominantParameter, AnimatorConditionMode.Equals, g) };
                    conditions.AddRange(comboOthers.Select(h => Condition(otherParameter, AnimatorConditionMode.NotEqual, h)));
                    Add(stateFor(dominantExpression, timeParameter), dominantExpression,
                        $"{dominantName} {GestureName(g)}", With(conditions.ToArray()));
                    continue;
                }

                for (var h = 0; h < GestureMapping.GestureCount; h++)
                {
                    if (comboOthers.Contains(h)) continue;

                    var otherExpression = set.FindExpression(otherTable[h]);
                    var target = otherExpression == null
                        ? neutral
                        : stateFor(otherExpression, h == (int)HandGesture.Fist && useOtherWeight ? otherWeight : null);
                    Add(target, otherExpression, $"{dominantName} {GestureName(g)}・{otherName} {GestureName(h)}",
                        With(Condition(dominantParameter, AnimatorConditionMode.Equals, g),
                            Condition(otherParameter, AnimatorConditionMode.Equals, h)));
                }
            }
        }

        /// <summary>
        /// モード（FEE/Mode）を選び直したら「表情選択」（FEE/Emote）を解除するレイヤー。今のモードをステートで覚えておき、
        /// 別のモードのステートに移ったときにパラメータードライバーで FEE/Emote を 0 にする（自分の画面でだけ動かし、値は同期される）。
        /// 動かすのはドライバーだけなので、ウェイトは 0。ステートには他のレイヤーと同じく空のクリップを入れ、Write Defaultsも揃える。
        /// </summary>
        private static VirtualLayer BuildEmoteReleaseLayer(CloneContext cloneContext, BuildPlan plan, bool writeDefaults)
        {
            var layer = VirtualLayer.Create(cloneContext, LayerPrefix + "Emote Release");
            layer.DefaultWeight = 0;
            var root = layer.StateMachine;
            var emptyClip = VirtualClip.Create("Empty");

            VirtualState AddState(string name, Vector3 position)
            {
                var state = root.AddState(name, emptyClip, position);
                state.WriteDefaultValues = writeDefaults;
                return state;
            }

            root.DefaultState = AddState("開始", new Vector3(300, 0, 0));

            var transitions = new List<VirtualStateTransition>();
            foreach (var mode in plan.Modes)
            {
                var state = AddState($"モード {mode.Value}: {mode.GestureSet.name}", new Vector3(600, 60 * mode.Value, 0));

                var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                driver.localOnly = true;
                driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                    name = BuildPlan.EmoteParameter,
                    value = 0,
                });
                state.Behaviours = ImmutableList.Create<StateMachineBehaviour>(driver);

                var transition = Transition(state, 0, Condition(BuildPlan.ModeParameter, AnimatorConditionMode.Equals, mode.Value));
                transition.Name = $"モード {mode.Value} を選んだ: 表情選択を解除";
                transitions.Add(transition);
            }
            root.AnyStateTransitions = transitions.ToImmutableList();
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
            List<VirtualLayer> expressionLayers, List<VirtualLayer> partLayers)
        {
            var result = new List<VirtualLayer>();
            var expressionInserted = false;
            var partsInserted = false;

            foreach (var layer in fx.Layers)
            {
                if (layer.IsOriginalLayer && set.originalGestureLayers.Contains(layer.Name))
                {
                    if (!expressionInserted) result.AddRange(expressionLayers);
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

            if (!expressionInserted) result.AddRange(expressionLayers);
            if (!partsInserted) result.InsertRange(result.IndexOf(expressionLayers[expressionLayers.Count - 1]) + 1, partLayers);

            fx.Layers = result;
        }
    }
}
