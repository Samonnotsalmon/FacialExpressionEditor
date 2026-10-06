using System;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情クリップに顔バリアントのベース顔を適用する。ビルドとプレビューで同じ処理を使う。
    /// 残すかどうかはシェイプキーごとに FaceVariant.ShouldKeep で決める。
    /// - リセット：クリップが動かしていなければ元Prefabの値にする（動かしていればクリップの値のまま）
    /// - 残す：クリップの値に「バリアントの値 − 元Prefabの値」を足す。動かしていなければバリアントの値にする
    /// 差し替えクリップはそのバリアント用に作ったものなので、差分は足さずにクリップの値をそのまま使う。
    /// </summary>
    internal static class BaseFaceProcessor
    {
        private const float BlendShapeMin = 0;
        private const float BlendShapeMax = 100;

        public static void Apply(FaceVariant variant, string expressionId, bool overridden, GameObject avatarRoot,
            Func<EditorCurveBinding, AnimationCurve> getCurve, Action<EditorCurveBinding, AnimationCurve> setCurve)
        {
            foreach (var key in variant.baseFace)
            {
                if (!key.enabled) continue;
                var keepBaseFace = variant.ShouldKeep(expressionId, key);

                var current = CurrentValue(avatarRoot, key);
                if (current == null) continue;

                var binding = EditorCurveBinding.FloatCurve(key.path, typeof(SkinnedMeshRenderer), "blendShape." + key.blendShape);
                var curve = getCurve(binding);

                if (curve == null)
                {
                    var value = keepBaseFace ? current.Value : key.referenceValue;
                    setCurve(binding, new AnimationCurve(new Keyframe(0, value)));
                }
                else if (keepBaseFace && !overridden)
                {
                    setCurve(binding, Offset(curve, current.Value - key.referenceValue));
                }
            }
        }

        private static float? CurrentValue(GameObject avatarRoot, BaseFaceKey key)
        {
            var transform = string.IsNullOrEmpty(key.path) ? avatarRoot.transform : avatarRoot.transform.Find(key.path);
            var renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
            if (renderer == null || renderer.sharedMesh == null) return null;

            var index = renderer.sharedMesh.GetBlendShapeIndex(key.blendShape);
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
