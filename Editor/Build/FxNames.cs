using System.Linq;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 生成するFXとアニメーション（レイヤー・ステート・遷移・クリップ）の名前。日本語などの2バイト文字は使わない。
    /// 表情やパーツの名前に英数字以外が含まれるときは、データ上の番号を使った名前にする（英数字の部分があれば後ろに付ける）。
    /// Expressionsメニューの名前は日本語のままでよい（こちらは使わない）。
    /// </summary>
    internal static class FxNames
    {
        public static string Expression(ExpressionSet set, Expression expression)
        {
            return Of(expression.name, $"Expression{set.expressions.IndexOf(expression)}");
        }

        public static string GestureSet(ExpressionSet set, GestureSet gestureSet)
        {
            return Of(gestureSet.name, $"Set{set.gestureSets.IndexOf(gestureSet)}");
        }

        public static string Part(ExpressionSet set, FacialPart part)
        {
            return Of(part.name, $"Part{set.parts.IndexOf(part)}");
        }

        public static string Group(string name, int index)
        {
            return Of(name, $"Group{index}");
        }

        public static string Of(string name, string fallback)
        {
            name = (name ?? "").Trim();
            if (name.Length > 0 && name.All(IsAscii)) return name;

            var ascii = new string(name.Where(IsAscii).ToArray()).Trim(' ', '_', '-');
            return ascii.Length > 0 ? $"{fallback}_{ascii}" : fallback;
        }

        private static bool IsAscii(char c)
        {
            return c >= 0x20 && c < 0x7F;
        }
    }
}
