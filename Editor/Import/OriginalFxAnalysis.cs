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
    /// - それ以外：そのまま／顔のカーブだけ取り除く／無効にする、を選べる
    /// 「顔」は、表情で動かすメッシュとベース顔のメッシュのシェイプキー。
    /// </summary>
    internal class OriginalFxAnalysis
    {
        public const string AfkParameter = "AFK";
        private const string BlendShapePrefix = "blendShape.";

        public HashSet<string> FacePaths = new HashSet<string>();
        public List<string> AfkLayers = new List<string>();
        // AFKの、顔を動かすクリップ。既定のステートから遷移をたどった順（イントロ→ループ→アウトロ）。
        public List<AnimationClip> AfkClips = new List<AnimationClip>();
        // 最初に見せるAFKのクリップ（いちばん長いもの。しなのならソファで寝ているループ）。
        public AnimationClip AfkPreviewClip;
        public List<string> FaceLayers = new List<string>();

        public static OriginalFxAnalysis Analyze(VRCAvatarDescriptor descriptor, ExpressionSet set, FaceVariant variant,
            IEnumerable<string> handledLayers)
        {
            var result = new OriginalFxAnalysis();
            if (descriptor == null || set == null) return result;

            result.FacePaths = FacePathsOf(descriptor, set, variant);
            var fx = FxImporter.GetFx(descriptor);
            if (fx == null) return result;

            var handled = new HashSet<string>(set.originalGestureLayers.Concat(set.originalPartLayers).Concat(handledLayers));
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
