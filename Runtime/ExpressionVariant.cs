using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// シーン上のアバターに付けるバリアント設定。使う表情セットと、このバリアントだけの差分を持つ。
    /// IEditorOnly なのでアップロード時には取り除かれる。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Samon/表情エディタ/表情バリアント")]
    public class ExpressionVariant : MonoBehaviour, IEditorOnly
    {
        public ExpressionSet expressionSet;

        public List<ExpressionOverride> overrides = new List<ExpressionOverride>();

        /// <summary>
        /// バリアントで差し替えていればそのクリップを、なければ共有のクリップを返す。
        /// </summary>
        public AnimationClip ResolveClip(Expression expression)
        {
            if (expression == null) return null;
            var found = overrides.Find(o => o.expressionId == expression.id);
            return found != null && found.clip != null ? found.clip : expression.clip;
        }
    }

    [Serializable]
    public class ExpressionOverride
    {
        public string expressionId;
        public AnimationClip clip;
    }
}
