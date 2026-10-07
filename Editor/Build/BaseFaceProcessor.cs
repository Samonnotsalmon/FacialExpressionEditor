using System;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情クリップに顔バリアントのベース顔を適用する。ビルドとプレビューで同じ処理を使う。
    /// - ベース顔はすべての表情に適用する：クリップの値に「バリアントの値 − 元Prefabの値」を足す。動かしていなければバリアントの値にする
    /// - 差し替えクリップはそのバリアント用に作ったものなので、差分は足さずにクリップの値をそのまま使う
    /// - 最後に、表情ごとのこの顔だけの値（表情の編集ウィンドウで変えたもの）で上書きする
    /// - 元FXのAFKのクリップは、ベース顔の後に、この顔だけの動き（キーを打ったシェイプキー）で上書きする
    /// </summary>
    internal static class BaseFaceProcessor
    {
        private const float BlendShapeMin = 0;
        private const float BlendShapeMax = 100;

        /// <summary>
        /// includeFaceValues が false なら、この表情だけの値は書き込まない（差し替えクリップにベース顔だけを書き込むとき）。
        /// </summary>
        public static void Apply(FaceVariant variant, string expressionId, bool overridden, GameObject avatarRoot,
            Func<EditorCurveBinding, AnimationCurve> getCurve, Action<EditorCurveBinding, AnimationCurve> setCurve,
            bool includeFaceValues = true)
        {
            if (!overridden)
            {
                foreach (var key in variant.baseFace)
                {
                    if (!key.enabled) continue;

                    var current = CurrentValue(avatarRoot, key.path, key.blendShape);
                    if (current == null) continue;

                    var binding = Binding(key.path, key.blendShape);
                    var curve = getCurve(binding);
                    setCurve(binding, curve == null
                        ? new AnimationCurve(new Keyframe(0, current.Value))
                        : Offset(curve, current.Value - key.referenceValue));
                }
            }

            var faceValues = includeFaceValues ? variant.FindFaceValues(expressionId) : null;
            if (faceValues == null) return;
            foreach (var value in faceValues.values)
            {
                if (CurrentValue(avatarRoot, value.path, value.blendShape) == null) continue;
                setCurve(Binding(value.path, value.blendShape), new AnimationCurve(new Keyframe(0, value.value)));
            }
        }

        /// <summary>
        /// 元FXのAFKのクリップに、この顔だけの動きを書き込む。ベース顔を適用した後に呼ぶ。
        /// </summary>
        public static void ApplyAfkCurves(AfkClipCurves curves, GameObject avatarRoot, Action<EditorCurveBinding, AnimationCurve> setCurve)
        {
            if (curves == null) return;
            foreach (var entry in curves.curves)
            {
                if (entry.curve == null || entry.curve.length == 0) continue;
                if (CurrentValue(avatarRoot, entry.path, entry.blendShape) == null) continue;
                setCurve(Binding(entry.path, entry.blendShape), new AnimationCurve(entry.curve.keys));
            }
        }

        private static EditorCurveBinding Binding(string path, string blendShape)
        {
            return EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + blendShape);
        }

        private static float? CurrentValue(GameObject avatarRoot, string path, string blendShape)
        {
            var transform = string.IsNullOrEmpty(path) ? avatarRoot.transform : avatarRoot.transform.Find(path);
            var renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
            if (renderer == null || renderer.sharedMesh == null) return null;

            var index = renderer.sharedMesh.GetBlendShapeIndex(blendShape);
            return index >= 0 ? renderer.GetBlendShapeWeight(index) : (float?)null;
        }

        private static AnimationCurve Offset(AnimationCurve curve, float delta)
        {
            var keys = curve.keys;
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i].value = Mathf.Clamp(keys[i].value + delta, BlendShapeMin, BlendShapeMax);
            }
            return new AnimationCurve(keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
        }
    }
}
