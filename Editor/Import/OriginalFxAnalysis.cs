using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 元FXのうち、置き換えていないのに顔を動かしているレイヤーを調べる。
    /// - AFK：AFK パラメータを条件に使い、顔を動かすレイヤー。元のアニメーションのまま、顔にだけベース顔と、この顔だけのAFKの動きを適用する
    /// - それ以外：そのまま／顔のカーブだけ取り除く／無効にする、を選べる。パーツらしいもの（メニューのトグルで切り替え、
    ///   顔のシェイプキーを少しだけ動かす。頬・涙・汗など）は、パーツとして取り込む候補にする
    /// 「顔」は、表情で動かすメッシュとベース顔のメッシュのシェイプキー。
    /// </summary>
    internal class OriginalFxAnalysis
    {
        public const string AfkParameter = "AFK";
        private const string BlendShapePrefix = "blendShape.";
        // パーツらしいレイヤーの、1つのクリップで動かす（0以外にする）顔のシェイプキーの上限。表情1つ分ほど動かすものはパーツではない。
        private const int PartMaxShapes = 10;

        public HashSet<string> FacePaths = new HashSet<string>();
        public List<string> AfkLayers = new List<string>();
        // AFKの、顔を動かすクリップ。既定のステートから遷移をたどった順（イントロ→ループ→アウトロ）。
        public List<AnimationClip> AfkClips = new List<AnimationClip>();
        // 最初に見せるAFKのクリップ（いちばん長いもの。しなのならソファで寝ているループ）。
        public AnimationClip AfkPreviewClip;
        public List<string> FaceLayers = new List<string>();
        // FaceLayers の、顔を動かすクリップ。
        public Dictionary<string, List<AnimationClip>> FaceLayerClips = new Dictionary<string, List<AnimationClip>>();
        // FaceLayers のうち、パーツらしいもの（パーツとして取り込む候補）。
        public List<string> PartCandidates = new List<string>();
        // FaceLayers の、顔を動かすクリップと、それを出すパラメータ（モーションタイムやブレンドツリーなら、値で少しずつ動く）。
        public Dictionary<string, List<FaceLayerClip>> FaceLayerTriggers = new Dictionary<string, List<FaceLayerClip>>();
        // パラメータが何で動くか（コンタクト・PhysBone・メニュー）。
        public Dictionary<string, string> ParameterSources = new Dictionary<string, string>();

        public class FaceLayerClip
        {
            public AnimationClip Clip;
            public string Parameter;
            public bool Gradual;
        }

        public static OriginalFxAnalysis Analyze(VRCAvatarDescriptor descriptor, ExpressionSet set, FaceVariant variant,
            IEnumerable<string> handledLayers)
        {
            var result = new OriginalFxAnalysis();
            if (descriptor == null || set == null) return result;

            result.FacePaths = FacePathsOf(descriptor, set, variant);
            var fx = FxImporter.GetFx(descriptor);
            if (fx == null) return result;

            var handled = new HashSet<string>(set.originalGestureLayers.Concat(set.originalPartLayers).Concat(handledLayers));
            var menuParameters = FxImporter.MenuParameters(descriptor);
            result.ParameterSources = ParameterSourcesOf(descriptor);
            foreach (var layer in fx.layers.Where(l => !handled.Contains(l.name)))
            {
                var faceClips = Clips(layer.stateMachine).Where(c => result.AnimatesFace(c)).ToList();
                if (faceClips.Count == 0) continue;

                if (FxImporter.AllTransitions(layer.stateMachine).Any(t => t.conditions.Any(c => c.parameter == AfkParameter)))
                {
                    result.AfkLayers.Add(layer.name);
                    var ordered = StatesInFlowOrder(layer.stateMachine).SelectMany(s => ClipsOf(s.motion)).Distinct()
                        .Where(faceClips.Contains);
                    result.AfkClips.AddRange(ordered.Where(c => !result.AfkClips.Contains(c)));
                    var longest = faceClips.OrderByDescending(c => c.length).First();
                    if (result.AfkPreviewClip == null || longest.length > result.AfkPreviewClip.length) result.AfkPreviewClip = longest;
                }
                else
                {
                    result.FaceLayers.Add(layer.name);
                    result.FaceLayerClips[layer.name] = faceClips;
                    result.FaceLayerTriggers[layer.name] = TriggersOf(layer.stateMachine, faceClips);

                    var parameters = FxImporter.AllTransitions(layer.stateMachine).SelectMany(t => t.conditions).Select(c => c.parameter).ToList();
                    if (parameters.Count > 0 && parameters.All(menuParameters.Contains) && faceClips.All(c => result.FaceShapeCount(c) <= PartMaxShapes))
                    {
                        result.PartCandidates.Add(layer.name);
                    }
                }
            }
            return result;
        }

        public static HashSet<string> FacePathsOf(VRCAvatarDescriptor descriptor, ExpressionSet set, FaceVariant variant)
        {
            var paths = new HashSet<string>(FaceVariantUtility.AnimatedBlendShapes(set).Select(a => a.path));
            if (variant != null) paths.UnionWith(variant.baseFace.Select(k => k.path));
            if (descriptor != null && descriptor.VisemeSkinnedMesh != null)
            {
                paths.Add(AnimationUtility.CalculateTransformPath(descriptor.VisemeSkinnedMesh.transform, descriptor.transform));
            }
            return paths;
        }

        public bool AnimatesFace(AnimationClip clip)
        {
            return AnimationUtility.GetCurveBindings(clip).Any(IsFaceCurve);
        }

        /// <summary>
        /// 顔を動かすクリップごとに、それを出すパラメータ。モーションタイムならそのパラメータ、ブレンドツリーならブレンドのパラメータ（どちらも値で少しずつ動く）、
        /// それ以外はそのステートへの遷移の条件のパラメータ。
        /// </summary>
        private static List<FaceLayerClip> TriggersOf(AnimatorStateMachine stateMachine, List<AnimationClip> faceClips)
        {
            var transitions = FxImporter.AllTransitions(stateMachine).ToList();
            var result = new List<FaceLayerClip>();
            foreach (var state in FxImporter.AllStates(stateMachine))
            {
                foreach (var clip in ClipsOf(state.motion).Where(faceClips.Contains))
                {
                    if (result.Any(r => r.Clip == clip)) continue;
                    var tree = state.motion as BlendTree;
                    var parameter = state.timeParameterActive ? state.timeParameter
                        : tree != null ? tree.blendParameter
                        : transitions.Where(t => t.destinationState == state).SelectMany(t => t.conditions).Select(c => c.parameter).FirstOrDefault();
                    result.Add(new FaceLayerClip { Clip = clip, Parameter = parameter, Gradual = state.timeParameterActive || tree != null });
                }
            }
            return result;
        }

        /// <summary>
        /// パラメータが何で動くか（コンタクト・PhysBone・Expressions メニュー）の短い説明。
        /// </summary>
        private static Dictionary<string, string> ParameterSourcesOf(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<string, string>();
            foreach (var receiver in descriptor.GetComponentsInChildren<VRCContactReceiver>(true))
            {
                if (string.IsNullOrEmpty(receiver.parameter) || result.ContainsKey(receiver.parameter)) continue;
                result[receiver.parameter] = receiver.receiverType == VRC.Dynamics.ContactReceiver.ReceiverType.Proximity
                    ? $"コンタクト「{receiver.name}」への近さ"
                    : $"コンタクト「{receiver.name}」に触れている";
            }
            foreach (var physBone in descriptor.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (string.IsNullOrEmpty(physBone.parameter)) continue;
                var name = physBone.name;
                result[physBone.parameter + "_IsGrabbed"] = $"PhysBone「{name}」をつかんでいる";
                result[physBone.parameter + "_IsPosed"] = $"PhysBone「{name}」をポーズ固定している";
                result[physBone.parameter + "_Angle"] = $"PhysBone「{name}」の角度";
                result[physBone.parameter + "_Stretch"] = $"PhysBone「{name}」の伸び";
                result[physBone.parameter + "_Squish"] = $"PhysBone「{name}」の縮み";
            }
            foreach (var (parameter, name) in FxImporter.MenuItemNames(descriptor))
            {
                if (!result.ContainsKey(parameter)) result[parameter] = $"メニュー「{name}」";
            }
            return result;
        }

        // クリップで0以外にする顔のシェイプキーの数。
        private int FaceShapeCount(AnimationClip clip)
        {
            return AnimationUtility.GetCurveBindings(clip)
                .Where(IsFaceCurve)
                .Count(b => AnimationUtility.GetEditorCurve(clip, b)?.keys.Any(k => Mathf.Abs(k.value) > 0.01f) == true);
        }

        public bool IsFaceCurve(EditorCurveBinding binding)
        {
            return binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith(BlendShapePrefix) && FacePaths.Contains(binding.path);
        }

        private static IEnumerable<AnimationClip> Clips(AnimatorStateMachine stateMachine)
        {
            return FxImporter.AllStates(stateMachine).SelectMany(s => ClipsOf(s.motion)).Distinct();
        }

        /// <summary>
        /// 既定のステートから遷移をたどった順のステート。たどれないステートは後ろに並べる。
        /// </summary>
        private static IEnumerable<AnimatorState> StatesInFlowOrder(AnimatorStateMachine root)
        {
            var all = FxImporter.AllStates(root).ToList();
            var visited = new HashSet<AnimatorState>();
            var queue = new Queue<AnimatorState>();
            void Enqueue(AnimatorState state)
            {
                if (state != null && visited.Add(state)) queue.Enqueue(state);
            }
            void EnqueueTarget(AnimatorTransitionBase transition)
            {
                Enqueue(transition.destinationState);
                if (transition.destinationStateMachine != null) Enqueue(transition.destinationStateMachine.defaultState);
            }

            Enqueue(root.defaultState);
            foreach (var transition in root.entryTransitions) EnqueueTarget(transition);
            foreach (var transition in root.anyStateTransitions) EnqueueTarget(transition);
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                yield return state;
                foreach (var transition in state.transitions) EnqueueTarget(transition);
            }
            foreach (var state in all.Where(s => !visited.Contains(s))) yield return state;
        }

        private static IEnumerable<AnimationClip> ClipsOf(Motion motion)
        {
            switch (motion)
            {
                case AnimationClip clip:
                    yield return clip;
                    break;
                case BlendTree tree:
                    foreach (var child in tree.children.SelectMany(c => ClipsOf(c.motion))) yield return child;
                    break;
            }
        }

        /// <summary>
        /// 条件に使える、アバターに元からあるコンタクト・PhysBoneのパラメータ（名前、Float かどうか、どこのものか）。
        /// </summary>
        public static List<(string parameter, bool isFloat, string source)> ContactParameters(VRCAvatarDescriptor descriptor)
        {
            var result = new List<(string, bool, string)>();
            if (descriptor == null) return result;

            foreach (var receiver in descriptor.GetComponentsInChildren<VRCContactReceiver>(true))
            {
                if (string.IsNullOrEmpty(receiver.parameter)) continue;
                var path = AnimationUtility.CalculateTransformPath(receiver.transform, descriptor.transform);
                var isFloat = receiver.receiverType == VRC.Dynamics.ContactReceiver.ReceiverType.Proximity;
                result.Add((receiver.parameter, isFloat, $"コンタクト（{path}）"));
            }

            foreach (var physBone in descriptor.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (string.IsNullOrEmpty(physBone.parameter)) continue;
                var path = AnimationUtility.CalculateTransformPath(physBone.transform, descriptor.transform);
                result.Add((physBone.parameter + "_IsGrabbed", false, $"PhysBone つかまれている（{path}）"));
                result.Add((physBone.parameter + "_IsPosed", false, $"PhysBone ポーズ固定（{path}）"));
                result.Add((physBone.parameter + "_Angle", true, $"PhysBone 角度（{path}）"));
                result.Add((physBone.parameter + "_Stretch", true, $"PhysBone 伸び（{path}）"));
                result.Add((physBone.parameter + "_Squish", true, $"PhysBone 縮み（{path}）"));
            }

            return result.GroupBy(p => p.Item1).Select(g => g.First()).ToList();
        }
    }
}
