using System;
using System.Collections.Generic;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// 左右それぞれ8種のジェスチャー表と、組み合わせ単位の上書き。
    /// 要素は Expression.id で、空文字は未割り当てを表す。
    /// </summary>
    [Serializable]
    public class GestureMapping
    {
        public const int GestureCount = 8;

        public Hand dominantHand = Hand.Right;

        // 添字は HandGesture の値。
        public string[] left = new string[GestureCount];
        public string[] right = new string[GestureCount];

        public List<GestureComboOverride> combos = new List<GestureComboOverride>();

        public string Resolve(HandGesture leftGesture, HandGesture rightGesture)
        {
            return Resolve(leftGesture, rightGesture, out _);
        }

        /// <summary>
        /// 組み合わせの上書き ＞ 優先する手 ＞ 反対の手 の順で表情を決める。
        /// どれにも割り当てがなければ null（無表情）を返す。
        /// source には、表情を決めた手を返す（組み合わせの上書きなら優先する手）。
        /// </summary>
        public string Resolve(HandGesture leftGesture, HandGesture rightGesture, out Hand source)
        {
            source = dominantHand;

            foreach (var combo in combos)
            {
                if (combo.left == leftGesture && combo.right == rightGesture && !string.IsNullOrEmpty(combo.expressionId))
                {
                    return combo.expressionId;
                }
            }

            var leftId = Get(left, leftGesture);
            var rightId = Get(right, rightGesture);
            var dominantId = dominantHand == Hand.Left ? leftId : rightId;
            var otherId = dominantHand == Hand.Left ? rightId : leftId;

            if (!string.IsNullOrEmpty(dominantId)) return dominantId;
            if (!string.IsNullOrEmpty(otherId))
            {
                source = dominantHand == Hand.Left ? Hand.Right : Hand.Left;
                return otherId;
            }
            return null;
        }

        public void EnsureSize()
        {
            if (left == null || left.Length != GestureCount) Array.Resize(ref left, GestureCount);
            if (right == null || right.Length != GestureCount) Array.Resize(ref right, GestureCount);
        }

        private static string Get(string[] table, HandGesture gesture)
        {
            var index = (int)gesture;
            return table != null && index < table.Length ? table[index] : null;
        }
    }

    [Serializable]
    public class GestureComboOverride
    {
        public HandGesture left;
        public HandGesture right;
        public string expressionId;
    }
}
