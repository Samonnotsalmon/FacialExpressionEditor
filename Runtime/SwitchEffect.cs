using System;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// 表情を固定したときの切り替え演出。間に別の表情（目閉じなど）を挟んでから、目的の表情に移る。
    /// 生成するクリップの頭に挟む表情を入れるだけなので、ステートや遷移は増えない。
    /// </summary>
    [Serializable]
    public class SwitchEffect
    {
        public bool enabled;

        // 間に挟む表情（Expression.id）。
        public string betweenExpressionId;

        // 挟む表情を見せる時間（秒）。
        public float holdTime = 0.1f;

        // 挟む表情から目的の表情へ移る時間（秒）。
        public float fadeTime = 0.08f;
    }

    public enum SwitchEffectMode
    {
        // 表情データの標準に従う。
        Default,
        // 演出なし。
        None,
        // この表情だけの設定を使う。
        Custom,
    }
}
