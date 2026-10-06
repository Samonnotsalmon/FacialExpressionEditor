using UnityEngine;
using VRC.SDKBase;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// シーン上のアバターに付ける表情設定。使う表情データと顔バリアントを指定する。
    /// IEditorOnly なのでアップロード時には取り除かれる。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Samon/表情エディタ/表情設定")]
    public class FacialExpressionAvatar : MonoBehaviour, IEditorOnly
    {
        public ExpressionSet expressionSet;

        // 未設定なら、ベース顔の処理と表情の差し替えをしない。
        public FaceVariant faceVariant;
    }
}
