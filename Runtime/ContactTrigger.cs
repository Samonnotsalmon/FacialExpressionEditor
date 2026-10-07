using System;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// アバターに元からあるコンタクト・PhysBoneのパラメータが条件を満たしている間、表情を出す。
    /// 表情選択（固定）より下、ジェスチャーより上の優先度で、一覧の上にあるものほど優先する。
    /// </summary>
    [Serializable]
    public class ContactTrigger
    {
        public string parameter;

        // Bool のパラメータはオンの間、Float のパラメータは threshold より大きい間。
        public bool isFloat;
        public float threshold = 0.5f;

        public string expressionId;
    }

    /// <summary>
    /// 顔を動かしている元FXのレイヤー（置き換えていないもの）の扱い。
    /// </summary>
    public enum OriginalLayerMode
    {
        // そのまま使う。
        Keep,
        // 顔のメッシュのシェイプキーのカーブだけ取り除き、ほかの演出は残す。
        StripFace,
        // レイヤーごと使わない。
        Disable,
    }

    [Serializable]
    public class OriginalLayerSetting
    {
        public string layerName;
        public OriginalLayerMode mode;
    }
}
