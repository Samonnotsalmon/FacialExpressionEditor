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
    /// まばたきと口モーフキャンセラーのシェイプキーは、表情データで「自分で編集」にしていなければ元アバターのもの（AvatarFaceDefaults）。
    /// 負荷を抑えるため、どの表情も止めないもの（全部オンのもの）は、パラメータもステートの指定も作らない。
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
        public bool ReplacesBlink => BlinkShapes.Count > 0;

        // どれかの表情が視線・リップシンクを止める（このときは全ステートで指定し直す）。
        public bool ControlsEyes { get; private set; }
        public bool ControlsMouth { get; private set; }

        // 口モーフキャンセラーで戻すシェイプキー（アバター上に見つかったもの）。
        public List<EditorCurveBinding> MouthMorphs { get; private set; } = new List<EditorCurveBinding>();

        // どれかの表情が口モーフキャンセラーを止める（このときは表情のステートから FEE/MouthCancel を切り替える）。
        public bool UsesMouthCancelParameter { get; private set; }

        public bool HasMouthCanceler => MouthMorphs.Count > 0;

        // 生成したものに置き換えるため、ビルドで取り除く元FXのレイヤー（まばたき・口モーフキャンセラー）。
        public List<string> BlinkLayersToReplace { get; private set; } = new List<string>();
        public List<string> MouthCancelerLayersToReplace { get; private set; } = new List<string>();

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
            var expressions = buildPlan.Modes.SelectMany(m => m.Emotes)
                .Concat(buildPlan.EmoteValues.Keys.Select(set.FindExpression))
                .Where(e => e != null)
                .Distinct()
                .ToList();

            if (set.replaceBlink && expressions.Any(e => !e.enableBlink))
            {
                var shapes = set.customBlink ? set.blinkShapes : defaults.BlinkShapes;
                plan.BlinkShapes = shapes
                    .Select(s => (Binding(s), s.closedValue))
                    .Where(s => AnimationUtility.GetFloatValue(avatarRoot, s.Item1, out _))
                    .ToList();
                if (plan.ReplacesBlink)
                {
                    if (!set.customBlink) plan.BlinkClip = defaults.BlinkClip;
                    if (defaults.BlinkLayer != null) plan.BlinkLayersToReplace.Add(defaults.BlinkLayer);
                }
            }

            plan.ControlsEyes = expressions.Any(e => !plan.EyesTracked(e));
            plan.ControlsMouth = expressions.Any(e => !e.enableLipSync);

            plan.MouthMorphs = (set.customMouthMorphs ? set.mouthMorphs : defaults.MouthMorphs)
                .Select(Binding)
                .Where(b => AnimationUtility.GetFloatValue(avatarRoot, b, out _))
                .Distinct()
                .ToList();
            plan.UsesMouthCancelParameter = plan.HasMouthCanceler && expressions.Any(e => !plan.CancelsMouth(e));

            // 元FXの口モーフキャンセラーは、自分で編集して空にしたときも取り除く（生成したものだけで扱う）。
            plan.MouthCancelerLayersToReplace = defaults.MouthCancelerLayers;
            return plan;
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
