using System;
using System.Collections.Generic;
using UnityEngine;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// ジト目・ツリ目などの顔バリアント。ベース顔と、表情ごとのこの顔だけの値、このバリアント専用の表情クリップを持つ。
    /// 同じ顔のアバター（服違いなど）は、シーンが違っても同じアセットを共有できる。
    /// ベース顔はすべての表情に適用する。表情ごとに変えたいところ（ウインクで閉じている目はジト目にしない、など）は、
    /// 表情の編集ウィンドウで「この顔のこの表情だけの値」として持つ。
    /// </summary>
    public class FaceVariant : ScriptableObject
    {
        public List<BaseFaceKey> baseFace = new List<BaseFaceKey>();

        // 表情ごとの、この顔だけの値（ベース顔のシェイプキーと、その左右別のシェイプキー）。
        public List<ExpressionFaceValues> faceValues = new List<ExpressionFaceValues>();

        public List<ExpressionOverride> overrides = new List<ExpressionOverride>();

        public ExpressionOverride FindOverride(string expressionId)
        {
            var found = overrides.Find(o => o.expressionId == expressionId);
            return found != null && found.clip != null ? found : null;
        }

        public ExpressionFaceValues FindFaceValues(string expressionId)
        {
            return faceValues.Find(v => v.expressionId == expressionId);
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

        // 元Prefabの値。表情のクリップの値に「バリアントの値 − この値」を足してベース顔を適用する。
        public float referenceValue;

        // 検出したときのバリアントでの値。別のアバターで使うときに、顔が同じかを確かめるために使う。
        public float variantValue;

        public bool enabled = true;

        public string Key => $"{path}|{blendShape}";
    }

    /// <summary>
    /// 1つの表情の、この顔だけの値。ベース顔を適用した後に、この値で上書きする。
    /// </summary>
    [Serializable]
    public class ExpressionFaceValues
    {
        public string expressionId;
        public List<BlendShapeValue> values = new List<BlendShapeValue>();

        public BlendShapeValue Find(string path, string blendShape)
        {
            return values.Find(v => v.path == path && v.blendShape == blendShape);
        }
    }

    [Serializable]
    public class ExpressionOverride
    {
        public string expressionId;
        public AnimationClip clip;

        // 複製したときの共有クリップのハッシュ。共有側が更新されたかの判定に使う。
        public string sourceHash;

        // ベース顔（差分）をクリップに反映済みか。二重に足さないための印。
        public bool baseFaceBaked;
    }
}
