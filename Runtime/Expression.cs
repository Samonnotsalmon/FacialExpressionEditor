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

        // ベース顔を残すかどうかは、顔ごとに変わるので FaceVariant 側で持つ。
        public bool enableBlink = true;
        public bool enableLipSync = true;
        public bool mouthMorphCancel = true;

        // Fistの握り具合で clip からブレンドする先。未設定ならブレンドしない。
        public AnimationClip leftTriggerClip;
        public AnimationClip rightTriggerClip;

        // オフなら ExpressionSet.defaultTransitionDuration を使う。
        public bool overrideTransitionDuration;
        public float transitionDuration = 0.1f;

        public static string NewId() => Guid.NewGuid().ToString("N");
    }
}
