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
    /// 元アバターのまばたきと口モーフキャンセラー。表情データで「自分で編集」にしていなければ、ビルドでこれを使う。
    /// - まばたき：元FXにまばたきのアニメーション（名前に blink / まばたき / 瞬き を含むレイヤーかクリップ）があればそれ、
    ///   無ければアバターの設定（Eye Look の Eyelids）のまばたきのシェイプキー
    /// - 口モーフキャンセラー：元FXの、Viseme を条件にしてシェイプキーだけを動かすレイヤー（しなのの Rip_OFF）
    /// どちらも、元FXのレイヤーは生成したものに置き換える。
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

        public List<string> MouthCancelerLayers = new List<string>();
        public List<BlendShapeRef> MouthMorphs = new List<BlendShapeRef>();
        public string MouthMorphSource = "（見つかりません）";

        public static AvatarFaceDefaults Find(VRCAvatarDescriptor descriptor, ExpressionSet set)
        {
            var result = new AvatarFaceDefaults();
            if (descriptor == null) return result;

            var fx = FxImporter.GetFx(descriptor);
            var candidates = fx == null
                ? new List<AnimatorControllerLayer>()
                : fx.layers.Where(l => !set.originalGestureLayers.Contains(l.name) && !set.originalPartLayers.Contains(l.name)).ToList();

            result.FindBlink(descriptor, candidates);
            result.FindMouthCanceler(descriptor, candidates);
            return result;
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
            if (binding == null) return;

            BlinkShapes = new List<BlinkShape>
            {
                new BlinkShape { path = binding.Value.path, blendShape = binding.Value.propertyName.Substring(BlendShapePrefix.Length) },
            };
            BlinkSource = "アバターの設定（Eye Look の Eyelids）";
        }

        private void FindMouthCanceler(VRCAvatarDescriptor descriptor, List<AnimatorControllerLayer> layers)
        {
            foreach (var layer in layers)
            {
                if (layer.name == BlinkLayer) continue;
                if (!FxImporter.AllTransitions(layer.stateMachine).Any(t => t.conditions.Any(c => c.parameter == FaceControlPlan.VisemeParameter))) continue;

                var clips = BlendShapeClips(layer, descriptor.gameObject);
                if (clips.Count == 0) continue;

                MouthCancelerLayers.Add(layer.name);
                foreach (var binding in clips.SelectMany(AnimationUtility.GetCurveBindings))
                {
                    var shape = binding.propertyName.Substring(BlendShapePrefix.Length);
                    if (MouthMorphs.Any(m => m.path == binding.path && m.blendShape == shape)) continue;
                    MouthMorphs.Add(new BlendShapeRef { path = binding.path, blendShape = shape });
                }
            }

            if (MouthCancelerLayers.Count > 0) MouthMorphSource = $"元FXの「{string.Join("」「", MouthCancelerLayers)}」レイヤー";
        }

        private static bool HasBlinkWord(string name)
        {
            return BlinkWords.Any(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// レイヤーのクリップ（ダミーを除く）。シェイプキー以外も動かすレイヤーなら、まばたきや口モーフキャンセラーではないので空。
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
