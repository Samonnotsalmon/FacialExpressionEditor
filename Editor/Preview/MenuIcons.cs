using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情メニューのアイコン。アイコンは表情エディタのサムネイルと同じものを、Library/ のキャッシュから使う。
    /// プレイモードへの切り替え中はUnityのプレビュー描画が使えないので、足りないものは編集モードのうちに描いておく。
    /// </summary>
    internal static class MenuIcons
    {
        /// <summary>
        /// アイコンが要る表情（表情選択に並ぶ表情）。
        /// </summary>
        public static List<Expression> Expressions(ExpressionSet set, BuildPlan plan)
        {
            return plan.EmoteValues.Keys.Select(set.FindExpression).Where(e => e != null).ToList();
        }

        /// <summary>
        /// 表情セットのモードがメニューに並ぶとき、そのアイコン（無表情）が要る。
        /// </summary>
        public static bool NeedsNeutral(BuildPlan plan)
        {
            return plan.UsesModeParameter && plan.Modes.Any(m => m.GestureSet != null);
        }

        /// <summary>
        /// 足りないアイコンを描いて Library/ のキャッシュに保存する。編集モードで呼ぶこと。戻り値は描いた数。
        /// </summary>
        public static int EnsureCached(GameObject avatarRoot, ExpressionSet set, FaceVariant variant)
        {
            var plan = BuildPlan.Create(set);
            var faceHash = ThumbnailKeys.FaceHash(avatarRoot);
            FacePreview preview = null;
            var rendered = 0;

            void Render(string key, AnimationClip clip)
            {
                if (preview == null) preview = new FacePreview(avatarRoot);
                preview.Apply(clip);
                var texture = preview.RenderStatic(ThumbnailCache.Size);
                ThumbnailCache.SaveToDisk(key, texture);
                Object.DestroyImmediate(texture);
                rendered++;
            }

            try
            {
                foreach (var expression in Expressions(set, plan))
                {
                    var key = ThumbnailKeys.Expression(expression, variant, faceHash);
                    if (ThumbnailCache.ExistsOnDisk(key)) continue;

                    var clip = PreviewClips.ForExpression(expression, variant, avatarRoot);
                    try
                    {
                        Render(key, clip);
                    }
                    finally
                    {
                        PreviewClips.Release(clip);
                    }
                }

                var neutralKey = ThumbnailKeys.Neutral(faceHash);
                if (NeedsNeutral(plan) && !ThumbnailCache.ExistsOnDisk(neutralKey)) Render(neutralKey, null);
            }
            finally
            {
                preview?.Dispose();
            }
            return rendered;
        }
    }
}
