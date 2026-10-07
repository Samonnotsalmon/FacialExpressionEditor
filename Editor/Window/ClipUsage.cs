using System;
using System.Collections.Generic;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// クリップがどこで使われているか（ジェスチャー、組み合わせの上書き、固定だけの項目、パーツ、トリガー）。
    /// ジェスチャーに割り当てた表情は「表情選択」に自動で並ぶので、それは使用箇所として数えない。
    /// どこにも使われていないクリップが「未割り当て」になる。使用箇所ごとに、そこから外す処理も持つ。
    /// </summary>
    internal sealed class ClipUsage
    {
        public static readonly string[] GestureShortLabels =
            { "Neutral", "Fist", "HandOpen", "FingerPoint", "Victory", "RockNRoll", "HandGun", "ThumbsUp" };

        public sealed class Use
        {
            public string Label;
            // 表情データからこの使用箇所を外す（Undoは呼び出し側で記録する）。
            public Action Remove;
        }

        private readonly Dictionary<AnimationClip, List<Use>> _uses = new Dictionary<AnimationClip, List<Use>>();
        private static readonly List<Use> None = new List<Use>();

        public static ClipUsage Compute(ExpressionSet set)
        {
            var usage = new ClipUsage();

            foreach (var gestureSet in set.gestureSets)
            {
                var mapping = gestureSet.mapping;
                mapping.EnsureSize();
                for (var i = 0; i < GestureMapping.GestureCount; i++)
                {
                    var index = i;
                    // 左右で同じジェスチャーに同じ表情なら、1か所（両手）として扱う。
                    if (!string.IsNullOrEmpty(mapping.left[i]) && mapping.left[i] == mapping.right[i])
                    {
                        usage.Add(set, mapping.left[i], $"{gestureSet.name} 両手 {GestureShortLabels[i]}",
                            () => { mapping.left[index] = null; mapping.right[index] = null; });
                        continue;
                    }
                    usage.Add(set, mapping.left[i], $"{gestureSet.name} 左手 {GestureShortLabels[i]}", () => mapping.left[index] = null);
                    usage.Add(set, mapping.right[i], $"{gestureSet.name} 右手 {GestureShortLabels[i]}", () => mapping.right[index] = null);
                }
                foreach (var combo in mapping.combos)
                {
                    usage.Add(set, combo.expressionId,
                        $"{gestureSet.name} {GestureShortLabels[(int)combo.left]} × {GestureShortLabels[(int)combo.right]}",
                        () => mapping.combos.Remove(combo));
                }
            }

            foreach (var (node, _) in ExpressionSetUtility.TreeOrder(set))
            {
                if (node.kind != MenuNodeKind.Expression) continue;
                var parent = set.menu.Find(n => n.id == node.parentId);
                var where = parent != null ? $"（{parent.name}）" : "";
                usage.Add(set, node.expressionId, $"固定だけの項目{where}", () => ExpressionSetUtility.RemoveNode(set, node));
            }

            foreach (var part in set.parts)
            {
                usage.Add(part.clip, $"パーツ：{part.name}", () => set.parts.Remove(part));
            }

            return usage;
        }

        public IReadOnlyList<Use> Of(AnimationClip clip)
        {
            return clip != null && _uses.TryGetValue(clip, out var list) ? list : None;
        }

        public bool IsUsed(AnimationClip clip) => Of(clip).Count > 0;

        /// <summary>
        /// 同じ表情が2か所以上に割り当てられているか（左右で同じジェスチャーは1か所と数える）。
        /// </summary>
        public bool IsDuplicated(AnimationClip clip) => Of(clip).Count > 1;

        private void Add(ExpressionSet set, string expressionId, string label, Action remove)
        {
            var expression = set.FindExpression(expressionId);
            if (expression != null) Add(expression.clip, label, remove);
        }

        private void Add(AnimationClip clip, string label, Action remove)
        {
            if (clip == null) return;
            if (!_uses.TryGetValue(clip, out var list)) _uses[clip] = list = new List<Use>();
            list.Add(new Use { Label = label, Remove = remove });
        }
    }
}
