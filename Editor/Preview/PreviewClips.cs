using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// プレビュー用のクリップを作る。ビルドと同じく、顔バリアントの差し替えとベース顔の処理を適用する。
    /// 作ったクリップは一時オブジェクトなので、使い終わったら Release で破棄する。
    /// </summary>
    internal static class PreviewClips
    {
        public static AnimationClip ForExpression(Expression expression, FaceVariant variant, GameObject avatarRoot)
        {
            var overridden = FaceVariantUtility.EffectiveOverride(variant, expression);
            var source = overridden != null ? overridden.clip : expression.clip;
            var clip = source != null ? Object.Instantiate(source) : new AnimationClip();
            clip.hideFlags = HideFlags.HideAndDontSave;

            if (variant != null)
            {
                BaseFaceProcessor.Apply(variant, expression.id, overridden != null, avatarRoot,
                    b => AnimationUtility.GetEditorCurve(clip, b),
                    (b, c) => AnimationUtility.SetEditorCurve(clip, b, c));
            }
            return clip;
        }

        /// <summary>
        /// Fistの握り具合で動かすクリップ（ビルドと同じ FistBlend）。握り具合0でベース顔、握り切ると表情。
        /// クリップ自体が時間で動く表情は、表情のクリップそのもの。
        /// </summary>
        public static AnimationClip ForFist(Expression expression, FaceVariant variant, GameObject avatarRoot)
        {
            var expressionClip = ForExpression(expression, variant, avatarRoot);
            if (IsTimeVarying(expressionClip)) return expressionClip;

            var clip = new AnimationClip { hideFlags = HideFlags.HideAndDontSave };
            FistBlend.FromBaseFace(ClipCurves.Of(expressionClip), avatarRoot, ClipCurves.Of(clip));
            Release(expressionClip);
            return clip;
        }

        /// <summary>
        /// パーツとして選んだプロパティだけを持つクリップ。
        /// </summary>
        public static AnimationClip ForPart(FacialPart part)
        {
            var clip = new AnimationClip { hideFlags = HideFlags.HideAndDontSave };
            if (part.clip == null) return clip;

            var only = new HashSet<string>(part.properties);
            foreach (var binding in AnimationUtility.GetCurveBindings(part.clip).Where(b => only.Contains(ExpressionClipBuilder.Key(b))))
            {
                AnimationUtility.SetEditorCurve(clip, binding, AnimationUtility.GetEditorCurve(part.clip, binding));
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(part.clip).Where(b => only.Contains(ExpressionClipBuilder.Key(b))))
            {
                AnimationUtility.SetObjectReferenceCurve(clip, binding, AnimationUtility.GetObjectReferenceCurve(part.clip, binding));
            }
            return clip;
        }

        /// <summary>
        /// 時間で値が変わるクリップ（Fistの握り具合で動かす目閉じなど）かどうか。
        /// </summary>
        public static bool IsTimeVarying(AnimationClip clip)
        {
            if (clip == null || clip.length <= 0) return false;
            return FistBlend.IsTimeVarying(ClipCurves.Of(clip));
        }

        public static void Release(AnimationClip clip)
        {
            if (clip != null && clip.hideFlags == HideFlags.HideAndDontSave) Object.DestroyImmediate(clip);
        }
    }
}
