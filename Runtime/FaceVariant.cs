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
        // 表情中にベース顔を残すかどうかの標準。初期値は「残す」（編集したベース顔を元の表情に適用する）。
        public bool keepBaseFaceByDefault = true;

        public List<BaseFaceKey> baseFace = new List<BaseFaceKey>();

        // 表情ごとに、シェイプキー単位で標準と違う扱いにしたもの。
        public List<ExpressionBaseFaceRule> expressionRules = new List<ExpressionBaseFaceRule>();

        public List<ExpressionOverride> overrides = new List<ExpressionOverride>();

        /// <summary>
        /// この表情で、このベース顔のシェイプキーを残すかどうか。
        /// 「常に残す」＞ 表情ごとの指定 ＞ 顔バリアントの標準 の順で決める。
        /// </summary>
        public bool ShouldKeep(string expressionId, BaseFaceKey key)
        {
            if (key.alwaysKeep) return true;
            var rule = FindRule(expressionId);
            if (rule != null)
            {
                if (rule.keepKeys.Contains(key.Key)) return true;
                if (rule.resetKeys.Contains(key.Key)) return false;
            }
            return keepBaseFaceByDefault;
        }

        public ExpressionBaseFaceRule FindRule(string expressionId)
        {
            return expressionRules.Find(r => r.expressionId == expressionId);
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

        // 元Prefabの値。「ベース顔を外す（リセット）」ときはこの値に戻す。
        public float referenceValue;

        // 検出したときのバリアントでの値。別のアバターで使うときに、顔が同じかを確かめるために使う。
        public float variantValue;

        public bool enabled = true;

        // 表情の設定に関係なく、常に残す（目の大きさなどの微調整向け）。
        public bool alwaysKeep;

        public string Key => $"{path}|{blendShape}";
    }

    /// <summary>
    /// 表情ごとの、ベース顔のシェイプキー単位の指定（標準と違うものだけ持つ）。
    /// </summary>
    [Serializable]
    public class ExpressionBaseFaceRule
    {
        public string expressionId;

        // 標準が「外す」でも、この表情では残すシェイプキー（BaseFaceKey.Key）。
        public List<string> keepKeys = new List<string>();

        // 標準が「残す」でも、この表情では外すシェイプキー（BaseFaceKey.Key）。
        public List<string> resetKeys = new List<string>();
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
