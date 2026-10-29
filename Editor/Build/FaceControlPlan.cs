using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情ごとのまばたき・視線・リップシンク・口モーフキャンセラーを、ビルドでどう扱うかを決める。
    /// まばたきはアバターの設定を参照し、口キャンセルは利用者が指定したクリップだけを使う。
    /// 口キャンセルの対象がなければ、専用レイヤーやパラメータは生成しない。
    /// メニュー生成とFX生成の両方のパスが同じ結果を使えるよう、表情データとアバターだけから決める。
    /// </summary>
    internal class FaceControlPlan
    {
        public const string BlinkParameter = BuildPlan.Prefix + "Blink";
        public const string MouthCancelParameter = BuildPlan.Prefix + "MouthCancel";
        public const string VisemeParameter = "Viseme";

        // まばたきのアニメーションで動かすシェイプキー。置き換えないなら空（VRChatのまばたきのまま）。
        public List<(EditorCurveBinding binding, float closedValue)> BlinkShapes { get; private set; } = new List<(EditorCurveBinding, float)>();
        // 元FXのまばたきのアニメーションをそのまま使うとき、そのクリップ。
        public AnimationClip BlinkClip { get; private set; }
        public bool ReplacesBlink => BlinkShapes.Count > 0 || BlinkClip != null;
        public AnimationClip MouthCancelClip { get; private set; }
        public List<MotionIntegrationRule> MotionRules { get; private set; }
        public Dictionary<string, AnimatorControllerParameterType> MotionParameters { get; private set; }
        public Dictionary<string, AnimatorControllerParameter> MotionParameterDefaults { get; private set; }
        public bool ApplyBaseFaceToAfk { get; private set; }

        // どれかの表情が視線・リップシンクを止める（このときは全ステートで指定し直す）。
        public bool ControlsEyes { get; private set; }
        public bool ControlsMouth { get; private set; }

        // 口モーフキャンセラーで戻すシェイプキー（アバター上に見つかったもの）。
        public List<EditorCurveBinding> MouthMorphs { get; private set; } = new List<EditorCurveBinding>();

        // どれかの表情が口モーフキャンセラーを止める（このときは表情のステートから FEE/MouthCancel を切り替える）。
        public bool UsesMouthCancelParameter { get; private set; }

        public bool HasMouthCanceler => MouthMorphs.Count > 0;

        // 自動で置き換える元FXのまばたきレイヤー。口キャンセルの置き換えは利用者のレイヤー選択に従う。
        public List<string> BlinkLayersToReplace { get; private set; } = new List<string>();

        // 元FXの、顔を動かしているレイヤーの扱い。AFK は顔にベース顔とこの顔だけのAFKの動きを適用し、
        // それ以外は表情データの指定に従って顔のカーブを取り除くか、レイヤーごと使わない。
        public HashSet<string> FacePaths { get; private set; } = new HashSet<string>();
        public HashSet<string> ReactionLayers { get; private set; } = new HashSet<string>();
        public List<string> AfkLayers { get; private set; } = new List<string>();
        public List<string> StripFaceLayers { get; private set; } = new List<string>();
        public List<string> DisabledLayers { get; private set; } = new List<string>();

        /// <summary>
        /// ビルド中は1回だけ作り、メニュー生成とFX生成で同じものを使う。
        /// FX生成の時点では元FXがアニメーターの仮想化で読めなくなるので、先に走るメニュー生成で作っておく。
        /// </summary>
        public static FaceControlPlan Get(nadena.dev.ndmf.BuildContext context, ExpressionSet set, BuildPlan buildPlan)
        {
            var state = context.GetState<BuildState>();
            return state.Plan ??= Create(set, buildPlan, context.AvatarRootObject);
        }

        private class BuildState
        {
            public FaceControlPlan Plan;
        }

        public static FaceControlPlan Create(ExpressionSet set, BuildPlan buildPlan, GameObject avatarRoot)
        {
            var plan = new FaceControlPlan();
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();

            var defaults = AvatarFaceDefaults.Find(descriptor, set);
            plan.MotionRules = MotionIntegration.Rules(set, descriptor).Where(r => r.enabled).ToList();
            plan.MotionParameters = MotionIntegration.Parameters(descriptor);
            plan.MotionParameterDefaults = MotionIntegration.Controllers(descriptor).SelectMany(s => s.controller.parameters).GroupBy(p => p.name).ToDictionary(g => g.Key, g => g.First());
            plan.ApplyBaseFaceToAfk = set.applyBaseFaceToAfk;
            var ruleErrors = MotionSetupWarnings(set, descriptor, defaults);
            if (ruleErrors.Count > 0) throw new System.InvalidOperationException("既存モーション連携の設定を確認してください：\n" + string.Join("\n", ruleErrors));
            var expressions = buildPlan.Modes.SelectMany(m => m.Emotes)
                .Concat(buildPlan.EmoteValues.Keys.Select(set.FindExpression))
                .Where(e => e != null)
                .Distinct()
                .ToList();

            // The editor warning and build validation use the same target resolution.
            var blinkIssue = BlinkSetupWarning(set, avatarRoot);
            if (blinkIssue != null) throw new System.InvalidOperationException(blinkIssue);
            if (set.UsesExistingBlink) plan.BlinkClip = set.blinkAnimation;
            plan.BlinkShapes = ResolveBlinkShapes(set, avatarRoot);
            if (plan.ReplacesBlink)
            {
                if (defaults.BlinkLayer != null) plan.BlinkLayersToReplace.Add(defaults.BlinkLayer);
            }

            // MMDから戻ったときも現在の表情の視線・リップシンク設定を復元する。
            plan.ControlsEyes = true;
            plan.ControlsMouth = true;

            plan.MouthMorphs = ResolveMouthShapes(set, avatarRoot);
            plan.MouthCancelClip = set.mouthCancelAnimation;
            plan.UsesMouthCancelParameter = plan.HasMouthCanceler && expressions.Any(e => !plan.CancelsMouth(e));

            var avatar = avatarRoot.GetComponentInChildren<FacialExpressionAvatar>(true);
            var handled = new List<string>();
            if (defaults.BlinkLayer != null) handled.Add(defaults.BlinkLayer);
            var analysis = OriginalFxAnalysis.Analyze(descriptor, set, avatar != null ? avatar.faceVariant : null, handled);
            plan.FacePaths = analysis.FacePaths;
            plan.FacePaths.UnionWith(set.faceMeshPaths);
            plan.ReactionLayers = new HashSet<string>(ReactionImporter.Scan(descriptor, set, avatar != null ? avatar.faceVariant : null).SelectMany(r => r.Layers));
            plan.AfkLayers = analysis.AfkLayers;
            foreach (var setting in set.originalLayerSettings.Where(s => analysis.FaceLayers.Contains(s.layerName)))
            {
                if (setting.mode == OriginalLayerMode.StripFace) plan.StripFaceLayers.Add(setting.layerName);
                else if (setting.mode == OriginalLayerMode.Disable) plan.DisabledLayers.Add(setting.layerName);
            }
            return plan;
        }

        internal static List<string> MotionSetupWarnings(ExpressionSet set, VRCAvatarDescriptor descriptor, AvatarFaceDefaults defaults)
        {
            var rules = MotionIntegration.Rules(set, descriptor).Where(r => r.enabled).ToList();
            var errors = MotionIntegration.Validate(set, descriptor, rules);
            foreach (var rule in rules.Where(r => r.sourcePlayable == "FX" && !string.IsNullOrEmpty(r.sourceLayer)))
                if (rule.sourceLayer == defaults.BlinkLayer)
                    errors.Add(rule.name + "：連携元が自動置換する瞬きレイヤーです。保持されるモーションのステートを選んでください。");
            return errors;
        }

        public static List<EditorCurveBinding> ResolveMouthShapes(ExpressionSet set, GameObject root)
        {
            if (set?.mouthCancelAnimation == null || root == null) return new List<EditorCurveBinding>();
            return AnimationUtility.GetCurveBindings(set.mouthCancelAnimation)
                .Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape.") &&
                    AnimationUtility.GetFloatValue(root, b, out _) && !LipSyncProtection.IsProtected(b, root, set))
                .ToList();
        }

        internal static string BlinkSetupWarning(ExpressionSet set, GameObject root)
        {
            if (set == null || root == null) return null;
            if (set.UsesExistingBlink)
                return set.blinkAnimation != null ? null : "瞬きのクリップが未指定です。「まばたき・口」でクリップを指定するか、専用の瞬きを新規作成してください。このままではビルドできません。";
            if (ResolveBlinkShapes(set, root).Count > 0) return null;
            return "瞬きが未設定、または対象のシェイプキーがアバターに見つかりません。「まばたき・口」でクリップを指定するか、専用の瞬きを作成して目閉じに使うシェイプキーを追加してください。このままではビルドできません。";
        }

        private static List<(EditorCurveBinding binding, float closedValue)> ResolveBlinkShapes(ExpressionSet set, GameObject root)
        {
            var shapes = set.customBlink ? set.blinkShapes : AvatarFaceDefaults.DescriptorBlinkShapes(root.GetComponent<VRCAvatarDescriptor>());
            if (set.UsesExistingBlink && set.blinkAnimation != null)
                shapes = AnimationUtility.GetCurveBindings(set.blinkAnimation)
                    .Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape."))
                    .Select(b => new BlinkShape { path = b.path, blendShape = b.propertyName.Substring(11), closedValue = 100 }).ToList();
            else if (set.blinkPose != null) shapes = BlinkPoseUtility.Shapes(set.blinkPose, root, set);
            return shapes.Select(s => (Binding(s), s.closedValue))
                .Where(s => AnimationUtility.GetFloatValue(root, s.Item1, out _)).ToList();
        }

        public bool IsFaceCurve(EditorCurveBinding binding)
        {
            return binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.") && FacePaths.Contains(binding.path);
        }

        public static EditorCurveBinding Binding(BlendShapeRef shape)
        {
            return EditorCurveBinding.FloatCurve(shape.path, typeof(SkinnedMeshRenderer), "blendShape." + shape.blendShape);
        }

        /// <summary>
        /// アバターの設定（Eye Look の Eyelids）にある、まばたきのシェイプキー。無ければ null。
        /// </summary>
        public static EditorCurveBinding? FindBlink(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) return null;
            var eyeLook = descriptor.customEyeLookSettings;
            if (eyeLook.eyelidType != VRCAvatarDescriptor.EyelidType.Blendshapes) return null;

            var renderer = eyeLook.eyelidsSkinnedMesh;
            var indices = eyeLook.eyelidsBlendshapes;
            if (renderer == null || renderer.sharedMesh == null || indices == null || indices.Length == 0) return null;
            if (indices[0] < 0 || indices[0] >= renderer.sharedMesh.blendShapeCount) return null;

            var path = AnimationUtility.CalculateTransformPath(renderer.transform, descriptor.transform);
            var name = renderer.sharedMesh.GetBlendShapeName(indices[0]);
            return EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + name);
        }

        // e が null なら無表情（すべてオン）。
        public bool BlinkOn(Expression e) => e == null || e.enableBlink;

        // まばたきを置き換えないときは、VRChatのまばたきを止めるには視線ごと止めるしかない。
        public bool EyesTracked(Expression e) => e == null || (e.enableEyeTracking && (ReplacesBlink || e.enableBlink));

        public bool MouthTracked(Expression e) => e == null || e.enableLipSync;

        // リップシンクを止めた表情では、口を戻す必要が無い（表情の口のまま）。
        public bool CancelsMouth(Expression e) => e == null || (e.mouthMorphCancel && e.enableLipSync);

        /// <summary>
        /// 表情のステートに付ける指定（視線・リップシンクの切り替えと、まばたき・口モーフキャンセラーのパラメータ）。
        /// ステートには前の表情に関係なく入るので、使うものはどのステートでも必ず指定する。
        /// </summary>
        public List<StateMachineBehaviour> BehavioursFor(Expression e)
        {
            var behaviours = new List<StateMachineBehaviour>();

            if (ControlsEyes || ControlsMouth)
            {
                var tracking = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
                if (ControlsEyes) tracking.trackingEyes = EyesTracked(e) ? VRC_AnimatorTrackingControl.TrackingType.Tracking : VRC_AnimatorTrackingControl.TrackingType.Animation;
                if (ControlsMouth) tracking.trackingMouth = MouthTracked(e) ? VRC_AnimatorTrackingControl.TrackingType.Tracking : VRC_AnimatorTrackingControl.TrackingType.Animation;
                behaviours.Add(tracking);
            }

            if (ReplacesBlink || UsesMouthCancelParameter)
            {
                // パラメータは同期しないので、どのプレイヤーの画面でも表情のステートから切り替える。
                var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                driver.localOnly = false;
                if (ReplacesBlink) driver.parameters.Add(Set(BlinkParameter, BlinkOn(e)));
                if (UsesMouthCancelParameter) driver.parameters.Add(Set(MouthCancelParameter, CancelsMouth(e)));
                behaviours.Add(driver);
            }

            return behaviours;
        }

        private static VRC_AvatarParameterDriver.Parameter Set(string name, bool value)
        {
            return new VRC_AvatarParameterDriver.Parameter
            {
                type = VRC_AvatarParameterDriver.ChangeType.Set,
                name = name,
                value = value ? 1 : 0,
            };
        }
    }
}
