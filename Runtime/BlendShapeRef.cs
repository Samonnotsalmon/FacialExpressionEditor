using System;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// アバター上のシェイプキー1つ（メッシュのパスとシェイプキー名）。
    /// </summary>
    [Serializable]
    public class BlendShapeRef
    {
        // アバターのルートからの相対パス。
        public string path;
        public string blendShape;

        public string Key => $"{path}|{blendShape}";
    }

    /// <summary>
    /// シェイプキーと、その値。
    /// </summary>
    [Serializable]
    public class BlendShapeValue : BlendShapeRef
    {
        public float value;
    }

    /// <summary>
    /// まばたきで動かすシェイプキーと、目を閉じたときの値。
    /// </summary>
    [Serializable]
    public class BlinkShape : BlendShapeRef
    {
        public float closedValue = 100f;
    }
}
