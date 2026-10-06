using System;
using System.Collections.Generic;
using UnityEngine;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// ジト目・ツリ目などの顔バリアント。ベース顔の扱いと、このバリアント専用の表情クリップを持つ。
    /// 同じ顔のアバター（服違いなど）は、シーンが違っても同じアセットを共有できる。
    /// </summary>
    public class FaceVariant : ScriptableObject
    {
        // 表情中にベース顔を残すかどうかの標準。表情ごとの例外は expressionRules で指定する。
        public bool keepBaseFaceByDefault;

        public List<BaseFaceKey> baseFace = new List<BaseFaceKey>();

        public List<ExpressionBaseFaceRule> expressionRules = new List<ExpressionBaseFaceRule>();

        public List<ExpressionOverride> overrides = new List<ExpressionOverride>();

        public bool ShouldKeepBaseFace(string expressionId)
        {
            var rule = expressionRules.Find(r => r.expressionId == expressionId);
            return rule != null ? rule.keepBaseFace : keepBaseFaceByDefault;
        }

        public ExpressionOverride FindOverride(string expressionId)
        {
            var found = overrides.Find(o => o.expressionId == expressionId);
            return found != null && found.clip != null ? found : null;
        }
    }

    /// <summary>
    /// ベース顔として扱うシェイプキー。バリアントでの値はビルド時にアバターから読む。
    /// </summary>
    [Serializable]
    public class BaseFaceKey
    {
        // アバターのルートからの相対パス。
        public string path;
        public string blendShape;

        // 元Prefabの値。「ベース顔をリセット」するときはこの値に戻す。
        public float referenceValue;

        // 検出したときのバリアントでの値。別のアバターで使うときに、顔が同じかを確かめるために使う。
        public float variantValue;

        public bool enabled = true;

        // 表情の設定に関係なく、常に残す（目の大きさなどの微調整向け）。
        public bool alwaysKeep;
    }

    /// <summary>
    /// 表情ごとの、ベース顔を残すかどうかの例外。
    /// </summary>
    [Serializable]
    public class ExpressionBaseFaceRule
    {
        public string expressionId;
        public bool keepBaseFace;
    }

    [Serializable]
    public class ExpressionOverride
    {
        public string expressionId;
        public AnimationClip clip;

        // 複製したときの共有クリップのハッシュ。共有側が更新されたかの判定に使う。
        public string sourceHash;

        // ベース顔（残すシェイプキーの差分）をクリップに反映済みか。二重に足さないための印。
        public bool baseFaceBaked;
    }
}
