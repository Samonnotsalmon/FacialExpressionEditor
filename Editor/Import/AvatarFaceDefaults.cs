using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 元アバターのまばたき。口キャンセルは利用者が指定するクリップと置き換えレイヤーだけを使う。
    /// - まばたき：元FXにまばたきのアニメーション（名前に blink / まばたき / 瞬き を含むレイヤーかクリップ）があればそれ、
    ///   無ければアバターの設定（Eye Look の Eyelids）のまばたきのシェイプキー
    /// 元FXのまばたきレイヤーは生成したものに置き換える。
    /// </summary>
    internal class AvatarFaceDefaults
    {
        private const string BlendShapePrefix = "blendShape.";
        private static readonly string[] BlinkWords = { "blink", "まばたき", "瞬き" };

        // 元FXのまばたきのレイヤーとアニメーション（無ければ null）。
        public string BlinkLayer;
        public AnimationClip BlinkClip;

        public List<BlinkShape> BlinkShapes = new List<BlinkShape>();
        public string BlinkSource = "（見つかりません）";

        public static AvatarFaceDefaults Find(VRCAvatarDescriptor descriptor, ExpressionSet set)
        {
            var result = new AvatarFaceDefaults();
            if (descriptor == null) return result;

            var fx = FxImporter.GetFx(descriptor);
            var candidates = fx == null
                ? new List<AnimatorControllerLayer>()
                : fx.layers.Where(l => !set.originalGestureLayers.Contains(l.name) && !set.originalPartLayers.Contains(l.name)).ToList();

            result.FindBlink(descriptor, candidates);
            return result;
        }

        public static List<BlinkShape> DescriptorBlinkShapes(VRCAvatarDescriptor descriptor)
        {
            var binding = FaceControlPlan.FindBlink(descriptor);
            return binding.HasValue ? new List<BlinkShape> { new BlinkShape
            {
                path = binding.Value.path,
                blendShape = binding.Value.propertyName.Substring(BlendShapePrefix.Length),
                closedValue = 100,
            } } : new List<BlinkShape>();
        }

        private void FindBlink(VRCAvatarDescriptor descriptor, List<AnimatorControllerLayer> layers)
        {
            foreach (var layer in layers)
            {
                var layerMatches = HasBlinkWord(layer.name);
                foreach (var clip in BlendShapeClips(layer, descriptor.gameObject))
                {
                    if (!layerMatches && !HasBlinkWord(clip.name)) continue;
                    if (!PreviewClips.IsTimeVarying(clip)) continue;

                    BlinkLayer = layer.name;
                    BlinkClip = clip;
                    BlinkShapes = AnimationUtility.GetCurveBindings(clip)
                        .Select(b => new BlinkShape
                        {
                            path = b.path,
                            blendShape = b.propertyName.Substring(BlendShapePrefix.Length),
                            closedValue = AnimationUtility.GetEditorCurve(clip, b).keys.Max(k => k.value),
                        })
                        .ToList();
                    BlinkSource = $"元FXの「{layer.name}」レイヤー（{clip.name}）";
                    return;
                }
            }

            var binding = FaceControlPlan.FindBlink(descriptor);
            if (binding == null)
            {
                // Eye Lookが未設定のアバターでも、明確な両目閉じのキーから生成する。
                var face = AvatarSetup.FaceRenderer(descriptor.gameObject);
                if (face == null || face.sharedMesh == null) return;
                foreach (var name in new[] { "vrc.blink", "blink", "eyeblink", "eye_close" })
                {
                    var index = Enumerable.Range(0, face.sharedMesh.blendShapeCount)
                        .Where(i => string.Equals(face.sharedMesh.GetBlendShapeName(i), name, StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty(-1).First();
                    if (index < 0) continue;
                    BlinkShapes.Add(new BlinkShape { path = AnimationUtility.CalculateTransformPath(face.transform, descriptor.transform), blendShape = face.sharedMesh.GetBlendShapeName(index), closedValue = 100 });
                    BlinkSource = "目閉じシェイプキーから生成（" + face.sharedMesh.GetBlendShapeName(index) + "）";
                    return;
                }
                return;
            }

            BlinkShapes = new List<BlinkShape>
            {
                new BlinkShape { path = binding.Value.path, blendShape = binding.Value.propertyName.Substring(BlendShapePrefix.Length) },
            };
            BlinkSource = "アバターの設定（Eye Look の Eyelids）";
        }

        private static bool HasBlinkWord(string name)
        {
            return BlinkWords.Any(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// レイヤーのクリップ（ダミーを除く）。シェイプキー以外も動かすレイヤーなら、まばたきではないので空。
        /// </summary>
        private static List<AnimationClip> BlendShapeClips(AnimatorControllerLayer layer, GameObject avatarRoot)
        {
            var clips = FxImporter.AllStates(layer.stateMachine)
                .Select(s => s.motion as AnimationClip)
                .Where(c => c != null && !FxImporter.IsDummyClip(c, avatarRoot))
                .Distinct()
                .ToList();
            if (clips.Count == 0) return clips;

            var onlyBlendShapes = clips.All(c =>
                AnimationUtility.GetObjectReferenceCurveBindings(c).Length == 0 &&
                AnimationUtility.GetCurveBindings(c).All(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith(BlendShapePrefix)));
            return onlyBlendShapes ? clips : new List<AnimationClip>();
        }
    }
}
