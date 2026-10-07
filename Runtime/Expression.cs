using System;
using UnityEngine;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// 表情1つ分の設定。設定はクリップ単位で持ち、どこに割り当てても同じ設定で使われる。
    /// </summary>
    [Serializable]
    public class Expression
    {
        // 割り当てやバリアントの差し替えから参照するためのID。名前を変えても変わらない。
        public string id = NewId();
        public string name;
        public AnimationClip clip;

        // 作者のクリップを編集するために複製したとき、その元のクリップ（元FXの取り込みやライブラリで、同じ表情として扱う）。
        public AnimationClip originalClip;

        // ベース顔を残すかどうかは、顔ごとに変わるので FaceVariant 側で持つ。
        // まばたき：オフなら、この表情の間はまばたきを止める（目を閉じる表情など）。
        public bool enableBlink = true;
        // 視線：オフなら、この表情の間は目の動き（VRChatの視線とまぶた）を止める。
        public bool enableEyeTracking = true;
        // リップシンク：オフなら、この表情の間は口を動かさない。
        public bool enableLipSync = true;
        // 口モーフキャンセラー：オンなら、話している間は口のシェイプキーをベース顔の値に戻す。
        public bool mouthMorphCancel = true;

        // オフなら ExpressionSet.defaultTransitionDuration を使う。
        public bool overrideTransitionDuration;
        public float transitionDuration = 0.1f;

        // 固定メニューでこの表情に切り替えたときの演出。
        public SwitchEffectMode fixedSwitchMode;
        public SwitchEffect fixedSwitchEffect = new SwitchEffect { enabled = true };

        public static string NewId() => Guid.NewGuid().ToString("N");
    }
}
