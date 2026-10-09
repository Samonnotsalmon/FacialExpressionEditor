using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
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
        private const string NeutralName = "Neutral";

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
            var avatarRoot = context.AvatarRootObject;
            var builder = new ExpressionClipBuilder(set, variant, avatarRoot);
            var cloneContext = controllerContext.CloneContext;
            var face = FaceControlPlan.Get(context, set, plan);
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (!string.IsNullOrEmpty(set.lipSyncMeshPath))
            {
                var mesh = avatarRoot.transform.Find(set.lipSyncMeshPath)?.GetComponent<SkinnedMeshRenderer>();
                if (mesh != null) descriptor.VisemeSkinnedMesh = mesh;
            }

            var expressionLayer = BuildExpressionLayer(cloneContext, set, plan, face, builder, writeDefaults);
            var expressionLayers = new List<VirtualLayer> { expressionLayer };
            if (plan.UsesModeParameter && plan.UsesEmoteParameter) expressionLayers.Add(BuildEmoteReleaseLayer(cloneContext, set, plan, writeDefaults));

            VirtualLayer blinkLayer = null;
            if (face.ReplacesBlink)
            {
                blinkLayer = BuildBlinkLayer(cloneContext, face, avatarRoot, writeDefaults);
                expressionLayers.Add(blinkLayer);
                DisableVrcBlink(avatarRoot.GetComponent<VRCAvatarDescriptor>());
            }

            var cancelerLayers = new List<VirtualLayer>();
            if (face.HasMouthCanceler) cancelerLayers.Add(BuildMouthCancelerLayer(cloneContext, face, avatarRoot, writeDefaults));

            var partLayers = BuildPartLayers(cloneContext, set, plan, builder, writeDefaults);

            EnsureParameter(fx, GestureLeft, AnimatorControllerParameterType.Int);
            EnsureParameter(fx, GestureRight, AnimatorControllerParameterType.Int);
            EnsureParameter(fx, GestureLeftWeight, AnimatorControllerParameterType.Float);
            EnsureParameter(fx, GestureRightWeight, AnimatorControllerParameterType.Float);
            if (plan.UsesModeParameter) EnsureParameter(fx, BuildPlan.ModeParameter, AnimatorControllerParameterType.Int);
            if (plan.UsesEmoteParameter) EnsureParameter(fx, BuildPlan.EmoteParameter, AnimatorControllerParameterType.Int);
            if (face.ReplacesBlink) EnsureParameter(fx, FaceControlPlan.BlinkParameter, AnimatorControllerParameterType.Bool, true);
            if (face.UsesMouthCancelParameter) EnsureParameter(fx, FaceControlPlan.MouthCancelParameter, AnimatorControllerParameterType.Bool, true);
            if (face.HasMouthCanceler) EnsureParameter(fx, FaceControlPlan.VisemeParameter, AnimatorControllerParameterType.Int);
            foreach (var entry in plan.Parts)
            {
                EnsureParameter(fx, entry.Parameter,
                    entry.IsGrouped ? AnimatorControllerParameterType.Int : AnimatorControllerParameterType.Bool);
            }
            foreach (var trigger in ContactTriggers(set))
            {
                EnsureParameter(fx, trigger.parameter, trigger.isFloat ? AnimatorControllerParameterType.Float : AnimatorControllerParameterType.Bool);
            }

            ProcessOriginalFaceLayers(fx, face, variant, avatarRoot);

            RedirectLayerControls(fx, set, new[] { expressionLayer, blinkLayer }.Where(l => l != null).ToList());
            // 元FXのまばたきのレイヤーは取り除くだけ（生成したまばたきは、表情レイヤーより後ろに置く必要があるため表情レイヤーの直後に入れる）。
            ReplaceOriginalLayers(fx, new List<(List<string>, List<VirtualLayer>)>
            {
                (set.originalGestureLayers, expressionLayers),
                (face.MouthCancelerLayersToReplace, cancelerLayers),
                (set.originalPartLayers, partLayers),
                (face.BlinkLayersToReplace, new List<VirtualLayer>()),
                (face.DisabledLayers, new List<VirtualLayer>()),
            });
            if (face.DanceParameter != null)
            {
                // Restarting Emote Release would clear the saved fixed face on return.
                var controlled = new[] { expressionLayer }.Concat(blinkLayer != null ? new[] { blinkLayer } : new VirtualLayer[0])
                    .Concat(cancelerLayers).Concat(partLayers).ToList();
                fx.Layers = fx.Layers.Concat(new[] { BuildDanceControl(cloneContext, face.DanceParameter, controlled) }).ToList();
            }
        }

        /// <summary>
        /// 元FXの、顔を動かしているレイヤーを加工する（ビルド用の複製だけを変え、元のアセットは変えない）。
        /// - AFK：元のアニメーションのまま、顔を動かすクリップにだけベース顔と、この顔だけのAFKの動きを適用する（体などの演出や、顔を動かさないステートには触らない）
        /// - 「顔のカーブだけ取り除く」にしたレイヤー：顔のシェイプキーのカーブを取り除く
        /// </summary>
        private static void ProcessOriginalFaceLayers(VirtualAnimatorController fx, FaceControlPlan face, FaceVariant variant, GameObject avatarRoot)
        {
            var done = new HashSet<VirtualClip>();
            foreach (var layer in fx.Layers.Where(l => l.IsOriginalLayer))
            {
                var afk = face.AfkLayers.Contains(layer.Name);
                var strip = face.StripFaceLayers.Contains(layer.Name);
                if (!afk && !strip) continue;

                foreach (var clip in layer.AllReachableNodes().OfType<VirtualState>().SelectMany(s => ClipsOf(s.Motion)))
                {
                    if (!done.Add(clip)) continue;
                    var faceCurves = clip.GetFloatCurveBindings().Where(face.IsFaceCurve).ToList();
                    if (faceCurves.Count == 0) continue;

                    if (strip)
                    {
                        foreach (var binding in faceCurves) clip.SetFloatCurve(binding, null);
                    }
                    else if (variant != null)
                    {
                        BaseFaceProcessor.Apply(variant, FaceVariant.AfkId, false, avatarRoot, clip.GetFloatCurve, clip.SetFloatCurve,
                            includeFaceValues: false);
                        BaseFaceProcessor.ApplyAfkCurves(AfkCurvesOf(variant, clip), avatarRoot, clip.SetFloatCurve);
                    }
                }
            }
        }

        // ビルド用に複製したクリップの、元のクリップ（NDMF が内部で持っている）。
        private static readonly PropertyInfo OriginalObjectProperty =
            typeof(VirtualNode).GetProperty("OriginalObject", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// ビルド用のクリップに対応する、この顔だけのAFKの動き。元のクリップで探し、分からなければクリップの名前で探す。
        /// </summary>
        private static AfkClipCurves AfkCurvesOf(FaceVariant variant, VirtualClip clip)
        {
            var original = OriginalObjectProperty?.GetValue(clip) as AnimationClip;
            return variant.FindAfkCurves(original) ?? variant.afkCurves.Find(c => c.clip != null && c.clip.name == clip.Name);
        }

        private static IEnumerable<VirtualClip> ClipsOf(VirtualMotion motion)
        {
            switch (motion)
            {
                case VirtualClip clip:
                    yield return clip;
                    break;
                case VirtualBlendTree tree:
                    foreach (var child in tree.Children.SelectMany(c => ClipsOf(c.Motion))) yield return child;
                    break;
            }
        }

        /// <summary>
        /// 使えるコンタクト・PhysBoneの表情（パラメータと表情があるもの）。上にあるものほど優先する。
        /// </summary>
        private static List<ContactTrigger> ContactTriggers(ExpressionSet set)
        {
            return set.contactTriggers.Where(t => !string.IsNullOrEmpty(t.parameter) && set.FindExpression(t.expressionId) != null).ToList();
        }

        private static AnimatorCondition ContactOn(ContactTrigger trigger)
        {
            return trigger.isFloat
                ? Condition(trigger.parameter, AnimatorConditionMode.Greater, trigger.threshold)
                : Condition(trigger.parameter, AnimatorConditionMode.If, 0);
        }

        private static AnimatorCondition ContactOff(ContactTrigger trigger)
        {
            return trigger.isFloat
                ? Condition(trigger.parameter, AnimatorConditionMode.Less, trigger.threshold)
                : Condition(trigger.parameter, AnimatorConditionMode.IfNot, 0);
        }

        /// <summary>
        /// 表情レイヤーを作る。人が見て追えるように、ルートに無表情（Neutral）、サブステートマシン「Gesture」と「Fixed」に
        /// 表情のステートを置き、遷移には「どの条件でどの表情になるか」が分かる名前を付ける。
        /// 名前は英数字だけにする（FxNames。日本語などの2バイト文字は使わない）。
        /// </summary>
        private static VirtualLayer BuildExpressionLayer(CloneContext cloneContext, ExpressionSet set, BuildPlan plan,
            FaceControlPlan face, ExpressionClipBuilder builder, bool writeDefaults)
        {
            var layer = VirtualLayer.Create(cloneContext, LayerPrefix + "Expression");
            var root = layer.StateMachine;
            var clips = new List<VirtualClip>();

            var neutralClip = builder.Build(new Expression { id = "__neutral__", name = NeutralName });
            clips.Add(neutralClip);
            var neutral = root.AddState(NeutralName, neutralClip, new Vector3(300, 120, 0));
            neutral.WriteDefaultValues = writeDefaults;
            neutral.Behaviours = face.BehavioursFor(null).ToImmutableList();
            root.DefaultState = neutral;

            var gestureMachine = VirtualStateMachine.Create(cloneContext, "Gesture");
            var fixedMachine = VirtualStateMachine.Create(cloneContext, "Fixed");
            root.StateMachines = root.StateMachines
                .Add(new VirtualStateMachine.VirtualChildStateMachine { StateMachine = gestureMachine, Position = new Vector3(600, 60, 0) })
                .Add(new VirtualStateMachine.VirtualChildStateMachine { StateMachine = fixedMachine, Position = new Vector3(600, 180, 0) });

            VirtualState AddState(VirtualStateMachine machine, string name, VirtualClip clip, int index, Expression expression)
            {
                clips.Add(clip);
                var state = machine.AddState(name, clip, new Vector3(300 + index % 4 * 240, index / 4 * 70, 0));
                state.WriteDefaultValues = writeDefaults;
                state.Behaviours = face.BehavioursFor(expression).ToImmutableList();
                return state;
            }

            // 同じ表情でも、Fistの握り具合で動かすかどうかで別のステートにする。
            // 握り具合で動かすときは、ベース顔から表情へ移るクリップを使う。
            var gestureStates = new Dictionary<(string id, string timeParameter), VirtualState>();
            VirtualState GestureState(Expression expression, string timeParameter)
            {
                if (gestureStates.TryGetValue((expression.id, timeParameter), out var state)) return state;

                var name = timeParameter == null
                    ? builder.NameOf(expression)
                    : $"{builder.NameOf(expression)} ({(timeParameter == GestureLeftWeight ? "Left" : "Right")} Grip)";
                var clip = timeParameter == null ? builder.Build(expression) : builder.BuildFist(expression);
                state = AddState(gestureMachine, name, clip, gestureStates.Count, expression);
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
                // 静止ポーズを経由する旧切り替え演出は、時間で動く表情を潰さないよう静止表情だけに適用する。
                if (!expression.freezeAnimation && PreviewClips.IsTimeVarying(expression.clip)) effect = null;
                var clip = effect != null
                    ? builder.BuildSwitch(expression, set.FindExpression(effect.betweenExpressionId), effect)
                    : builder.Build(expression);
                return fixedStates[expression.id] = AddState(fixedMachine, builder.NameOf(expression), clip, fixedStates.Count, expression);
            }

            // 「表情選択」で選んだ表情（FEE/Emote）を優先し、選んでいない（0）ときだけモードの遷移が働く。
            // FaceEmoと同じく後から選んだほうが勝つように、モードを選び直すと「表情選択の解除」レイヤーが FEE/Emote を 0 に戻す。
            foreach (var pair in plan.EmoteValues)
            {
                var expression = set.FindExpression(pair.Key);
                var transition = Transition(FixedState(expression), set.GetTransitionDuration(expression),
                    Condition(BuildPlan.EmoteParameter, AnimatorConditionMode.Equals, pair.Value));
                transition.Name = $"Emote {pair.Value}: {builder.NameOf(expression)}";
                transitions.Add(transition);
            }

            // コンタクト・PhysBone：表情選択より下、ジェスチャーより上。上にあるものほど優先し、
            // 条件が重ならないよう、それより上のものが成り立っていない条件を付ける。
            var triggers = ContactTriggers(set);
            if (triggers.Count > 0)
            {
                var contactMachine = VirtualStateMachine.Create(cloneContext, "Contact");
                root.StateMachines = root.StateMachines
                    .Add(new VirtualStateMachine.VirtualChildStateMachine { StateMachine = contactMachine, Position = new Vector3(600, 300, 0) });

                var contactStates = new Dictionary<string, VirtualState>();
                for (var i = 0; i < triggers.Count; i++)
                {
                    var trigger = triggers[i];
                    var expression = set.FindExpression(trigger.expressionId);
                    if (!contactStates.TryGetValue(expression.id, out var state))
                    {
                        state = AddState(contactMachine, builder.NameOf(expression), builder.Build(expression), contactStates.Count, expression);
                        contactStates[expression.id] = state;
                    }

                    var conditions = new List<AnimatorCondition>();
                    if (plan.UsesEmoteParameter) conditions.Add(Condition(BuildPlan.EmoteParameter, AnimatorConditionMode.Equals, 0));
                    conditions.AddRange(triggers.Take(i).Select(ContactOff));
                    conditions.Add(ContactOn(trigger));
                    var transition = Transition(state, set.GetTransitionDuration(expression), conditions.ToArray());
                    transition.Name = $"Contact {i}: {FxNames.Of(trigger.parameter, $"Parameter{i}")} -> {builder.NameOf(expression)}";
                    transitions.Add(transition);
                }
            }

            // メニューで選んだモード（FEE/Mode の値）ごとに遷移を作る。モードは1つだけ有効になるので条件は重ならない。
            // コンタクト・PhysBoneがどれも成り立っていないときだけ働く。
            foreach (var mode in plan.Modes)
            {
                var prefix = new List<AnimatorCondition>();
                if (plan.UsesEmoteParameter) prefix.Add(Condition(BuildPlan.EmoteParameter, AnimatorConditionMode.Equals, 0));
                if (plan.UsesModeParameter) prefix.Add(Condition(BuildPlan.ModeParameter, AnimatorConditionMode.Equals, mode.Value));
                prefix.AddRange(triggers.Select(ContactOff));

                var label = plan.UsesModeParameter ? $"Mode {mode.Value} {FxNames.GestureSet(set, mode.GestureSet)}: " : "";
                AddGestureTransitions(set, builder, mode.GestureSet.mapping, prefix, label, neutral, GestureState, transitions);
            }
            root.AnyStateTransitions = transitions.ToImmutableList();

            // まばたきと口モーフキャンセラーのレイヤーが止まっている間は、この表情レイヤーの値が見えるようにする。
            if (!writeDefaults)
            {
                builder.FillMissingWithDefaults(clips, face.MouthMorphs.Concat(face.BlinkShapes.Select(s => s.binding)));
            }
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
        private static void AddGestureTransitions(ExpressionSet set, ExpressionClipBuilder builder, GestureMapping mapping,
            List<AnimatorCondition> prefix, string label, VirtualState neutral, System.Func<Expression, string, VirtualState> stateFor,
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
            var dominantName = dominantIsLeft ? "Left" : "Right";
            var otherName = dominantIsLeft ? "Right" : "Left";
            string GestureName(int g) => ClipUsage.GestureShortLabels[g];

            AnimatorCondition[] With(params AnimatorCondition[] conditions) => prefix.Concat(conditions).ToArray();

            void Add(VirtualState target, Expression expression, string name, AnimatorCondition[] conditions)
            {
                var transition = Transition(target, set.GetTransitionDuration(expression), conditions);
                transition.Name = $"{label}{name} -> {(expression != null ? builder.NameOf(expression) : NeutralName)}";
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
                    $"Left {GestureName((int)combo.left)} x Right {GestureName((int)combo.right)}",
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
                    Add(target, otherExpression, $"{dominantName} {GestureName(g)}, {otherName} {GestureName(h)}",
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
        private static VirtualLayer BuildEmoteReleaseLayer(CloneContext cloneContext, ExpressionSet set, BuildPlan plan, bool writeDefaults)
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

            root.DefaultState = AddState("Start", new Vector3(300, 0, 0));

            var transitions = new List<VirtualStateTransition>();
            foreach (var mode in plan.Modes)
            {
                var state = AddState($"Mode {mode.Value} {FxNames.GestureSet(set, mode.GestureSet)}", new Vector3(600, 60 * mode.Value, 0));

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
                transition.Name = $"Mode {mode.Value} selected: clear Emote";
                transitions.Add(transition);
            }
            root.AnyStateTransitions = transitions.ToImmutableList();
            return layer;
        }

        /// <summary>
        /// VRChatのまばたきの代わりに、まばたきのアニメーションを流すレイヤー。表情のステートが FEE/Blink を切り替え、
        /// まばたきを止める表情の間は「止める」（何もしない）にする。視線（目の動き）はVRChatのまま動く。
        /// 元FXのまばたきのアニメーションを使うときはそのまま、それ以外はシェイプキーごとにまばたきのカーブを作る。
        /// </summary>
        private static VirtualLayer BuildBlinkLayer(CloneContext cloneContext, FaceControlPlan face, GameObject avatarRoot, bool writeDefaults)
        {
            var layer = VirtualLayer.Create(cloneContext, LayerPrefix + "Blink");
            var root = layer.StateMachine;

            var blinkClip = VirtualClip.Create(face.BlinkClip != null ? FxNames.Of(face.BlinkClip.name, "Blink") : "Blink");
            var settings = blinkClip.Settings;
            settings.loopTime = true;
            blinkClip.Settings = settings;
            if (face.BlinkClip != null)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(face.BlinkClip))
                {
                    blinkClip.SetFloatCurve(binding, AnimationUtility.GetEditorCurve(face.BlinkClip, binding));
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(face.BlinkClip))
                    blinkClip.SetObjectCurve(binding, AnimationUtility.GetObjectReferenceCurve(face.BlinkClip, binding));
            }
            else
            {
                foreach (var (binding, closedValue) in face.BlinkShapes)
                {
                    AnimationUtility.GetFloatValue(avatarRoot, binding, out var open);
                    blinkClip.SetFloatCurve(binding, BlinkCurve(open, closedValue));
                }
            }

            var blink = root.AddState("Blink", blinkClip, new Vector3(300, 0, 0));
            var stop = root.AddState("Stop", VirtualClip.Create("Stop"), new Vector3(300, 80, 0));
            blink.WriteDefaultValues = writeDefaults;
            stop.WriteDefaultValues = writeDefaults;
            root.DefaultState = blink;

            blink.Transitions = ImmutableList.Create(Transition(stop, 0,
                Condition(FaceControlPlan.BlinkParameter, AnimatorConditionMode.IfNot, 0)));
            stop.Transitions = ImmutableList.Create(Transition(blink, 0,
                Condition(FaceControlPlan.BlinkParameter, AnimatorConditionMode.If, 0)));
            return layer;
        }

        /// <summary>
        /// まばたきのカーブ。毎回同じ間隔だと機械的に見えるので、10秒の中で間隔を変えて3回まばたきする。
        /// </summary>
        private static AnimationCurve BlinkCurve(float open, float closed)
        {
            var keys = new List<Keyframe> { new Keyframe(0, open) };
            foreach (var start in new[] { 2.4f, 5.9f, 8.6f })
            {
                keys.Add(new Keyframe(start, open));
                keys.Add(new Keyframe(start + 0.06f, closed));
                keys.Add(new Keyframe(start + 0.1f, closed));
                keys.Add(new Keyframe(start + 0.2f, open));
            }
            keys.Add(new Keyframe(10f, open));

            var curve = new AnimationCurve(keys.ToArray());
            for (var i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            return curve;
        }

        /// <summary>
        /// アバターの設定から、VRChatのまばたき（Eyelids の Blink）だけを外す。視線で動くまぶた（上下を見る）は残す。
        /// </summary>
        private static void DisableVrcBlink(VRCAvatarDescriptor descriptor)
        {
            if (FaceControlPlan.FindBlink(descriptor) == null) return;

            var eyeLook = descriptor.customEyeLookSettings;
            var indices = (int[])eyeLook.eyelidsBlendshapes.Clone();
            indices[0] = -1;
            eyeLook.eyelidsBlendshapes = indices;
            descriptor.customEyeLookSettings = eyeLook;
        }

        /// <summary>
        /// 口モーフキャンセラー。話している間（Viseme が 0 以外）、口のシェイプキーをアバターの今の値（ベース顔）に戻して、
        /// 表情の口とリップシンクの口が重ならないようにする。口モーフキャンセラーを止める表情の間は働かない。
        /// </summary>
        private static VirtualLayer BuildMouthCancelerLayer(CloneContext cloneContext, FaceControlPlan face, GameObject avatarRoot, bool writeDefaults)
        {
            var layer = VirtualLayer.Create(cloneContext, LayerPrefix + "Mouth Morph Canceler");
            var root = layer.StateMachine;

            var cancelClip = VirtualClip.Create("Cancel Mouth");
            foreach (var binding in face.MouthMorphs)
            {
                AnimationUtility.GetFloatValue(avatarRoot, binding, out var value);
                if (face.MouthCancelClip != null)
                {
                    var curve = AnimationUtility.GetEditorCurve(face.MouthCancelClip, binding);
                    if (curve != null) value = curve.Evaluate(0);
                }
                cancelClip.SetFloatCurve(binding, new AnimationCurve(new Keyframe(0, value)));
            }

            var idle = root.AddState("Not Speaking", VirtualClip.Create("Not Speaking"), new Vector3(300, 0, 0));
            var cancel = root.AddState("Cancel Mouth", cancelClip, new Vector3(300, 80, 0));
            idle.WriteDefaultValues = writeDefaults;
            cancel.WriteDefaultValues = writeDefaults;
            root.DefaultState = idle;

            var speaking = new List<AnimatorCondition> { Condition(FaceControlPlan.VisemeParameter, AnimatorConditionMode.NotEqual, 0) };
            if (face.UsesMouthCancelParameter) speaking.Add(Condition(FaceControlPlan.MouthCancelParameter, AnimatorConditionMode.If, 0));
            idle.Transitions = ImmutableList.Create(Transition(cancel, 0, speaking.ToArray()));

            var stops = new List<VirtualStateTransition>
            {
                Transition(idle, 0, Condition(FaceControlPlan.VisemeParameter, AnimatorConditionMode.Equals, 0)),
            };
            if (face.UsesMouthCancelParameter)
            {
                stops.Add(Transition(idle, 0, Condition(FaceControlPlan.MouthCancelParameter, AnimatorConditionMode.IfNot, 0)));
            }
            cancel.Transitions = stops.ToImmutableList();
            return layer;
        }

        /// <summary>
        /// 元FXの他のレイヤー（AFKなど）が、置き換えるジェスチャーレイヤーのウェイトを切り替えていたら、
        /// 生成した表情レイヤー（とまばたきのレイヤー）を切り替えるように付け替える。そのままだとビルドで消えてしまう。
        /// </summary>
        private static void RedirectLayerControls(VirtualAnimatorController fx, ExpressionSet set, List<VirtualLayer> targets)
        {
            var replaced = new HashSet<int>(fx.Layers
                .Where(l => l.IsOriginalLayer && set.originalGestureLayers.Contains(l.Name))
                .Select(l => l.VirtualLayerIndex));
            if (replaced.Count == 0) return;

            foreach (var state in fx.Layers.SelectMany(l => l.AllReachableNodes().OfType<VirtualState>()))
            {
                if (!state.Behaviours.OfType<VRCAnimatorLayerControl>().Any(IsReplaced)) continue;

                var behaviours = new List<StateMachineBehaviour>();
                foreach (var behaviour in state.Behaviours)
                {
                    if (!(behaviour is VRCAnimatorLayerControl control) || !IsReplaced(control))
                    {
                        behaviours.Add(behaviour);
                        continue;
                    }

                    foreach (var target in targets)
                    {
                        // 左手・右手のレイヤーをそれぞれ切り替えていたものは、1つにまとめる。
                        var exists = behaviours.OfType<VRCAnimatorLayerControl>().Any(c =>
                            c.playable == control.playable && c.layer == target.VirtualLayerIndex && Mathf.Approximately(c.goalWeight, control.goalWeight));
                        if (exists) continue;

                        var copy = Object.Instantiate(control);
                        copy.layer = target.VirtualLayerIndex;
                        behaviours.Add(copy);
                    }
                }
                state.Behaviours = behaviours.ToImmutableList();
            }

            bool IsReplaced(VRCAnimatorLayerControl c) => c.playable == VRC_AnimatorLayerControl.BlendableLayer.FX && replaced.Contains(c.layer);
        }

        private static List<VirtualLayer> BuildPartLayers(CloneContext cloneContext, ExpressionSet set, BuildPlan plan,
            ExpressionClipBuilder builder, bool writeDefaults)
        {
            var layers = new List<VirtualLayer>();
            var duration = set.defaultTransitionDuration;

            foreach (var entry in plan.Parts.Where(p => !p.IsGrouped))
            {
                var layer = VirtualLayer.Create(cloneContext, LayerPrefix + "Part " + FxNames.Part(set, entry.Part));
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

            for (var g = 0; g < plan.PartGroups.Count; g++)
            {
                var group = plan.PartGroups[g];
                var layer = VirtualLayer.Create(cloneContext, LayerPrefix + "Parts " + FxNames.Group(group.Name, g));
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
                    var state = stateMachine.AddState(FxNames.Part(set, entry.Part), clip, new Vector3(600, 60 * entry.Value, 0));
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

        private static VirtualLayer BuildDanceControl(CloneContext context, string parameter, List<VirtualLayer> targets)
        {
            foreach (var target in targets)
            {
                // Stop state behaviours as well as curves while the avatar's own dance owns the face.
                foreach (var transition in target.AllReachableNodes().OfType<VirtualStateTransition>())
                    transition.Conditions = transition.Conditions.Add(Condition(parameter, AnimatorConditionMode.Equals, 0));
                var machine = target.StateMachine;
                var resume = machine.DefaultState;
                var suspended = machine.AddState("Dance — suspend face", VirtualClip.Create("Dance suspend"), new Vector3(0, -100, 0));
                suspended.WriteDefaultValues = false;
                suspended.Transitions = ImmutableList.Create(Transition(resume, 0,
                    Condition(parameter, AnimatorConditionMode.Equals, 0)));
                machine.AnyStateTransitions = machine.AnyStateTransitions.Insert(0, Transition(suspended, 0,
                    Condition(parameter, AnimatorConditionMode.NotEqual, 0)));
            }

            var layer = VirtualLayer.Create(context, LayerPrefix + "Dance priority");
            var sm = layer.StateMachine;
            var active = sm.AddState("Normal", VirtualClip.Create("Normal"), Vector3.zero);
            var dance = sm.AddState("Dance", VirtualClip.Create("Dance"), new Vector3(300, 0, 0));
            active.WriteDefaultValues = dance.WriteDefaultValues = false;
            sm.DefaultState = active;
            active.Transitions = ImmutableList.Create(Transition(dance, 0, Condition(parameter, AnimatorConditionMode.NotEqual, 0)));
            dance.Transitions = ImmutableList.Create(Transition(active, 0, Condition(parameter, AnimatorConditionMode.Equals, 0)));
            foreach (var pair in new[] { (state: active, weight: 1f), (state: dance, weight: 0f) })
            {
                pair.state.Behaviours = targets.Select(target =>
                {
                    var control = ScriptableObject.CreateInstance<VRCAnimatorLayerControl>();
                    control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
                    control.layer = target.VirtualLayerIndex;
                    control.goalWeight = pair.weight;
                    control.blendDuration = 0;
                    return (StateMachineBehaviour)control;
                }).ToImmutableList();
            }
            return layer;
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

        private static void EnsureParameter(VirtualAnimatorController fx, string name, AnimatorControllerParameterType type, bool defaultBool = false)
        {
            if (fx.Parameters.ContainsKey(name)) return;
            fx.SetParameter(name, new AnimatorControllerParameter { name = name, type = type, defaultBool = defaultBool });
        }

        /// <summary>
        /// 元FXのレイヤー（ジェスチャー・口モーフキャンセラー・パーツ）を取り除き、それぞれ最初にあった位置に生成レイヤーを入れる。
        /// 他のレイヤーとの重なり順は元のまま保たれる。元が無いものは、前のグループの生成レイヤーの直後（最初のグループは最後尾）に入れる。
        /// </summary>
        private static void ReplaceOriginalLayers(VirtualAnimatorController fx, List<(List<string> originals, List<VirtualLayer> generated)> groups)
        {
            var result = new List<VirtualLayer>();
            var inserted = new bool[groups.Count];

            foreach (var layer in fx.Layers)
            {
                var g = groups.FindIndex(x => x.originals.Contains(layer.Name));
                if (g < 0)
                {
                    result.Add(layer);
                    continue;
                }

                if (!inserted[g]) result.AddRange(groups[g].generated);
                inserted[g] = true;
            }

            for (var g = 0; g < groups.Count; g++)
            {
                if (inserted[g] || groups[g].generated.Count == 0) continue;

                var anchor = groups.Take(g).SelectMany(x => x.generated).Select(l => result.IndexOf(l)).DefaultIfEmpty(-1).Max();
                if (anchor < 0) result.AddRange(groups[g].generated);
                else result.InsertRange(anchor + 1, groups[g].generated);
            }

            fx.Layers = result;
        }
    }
}
