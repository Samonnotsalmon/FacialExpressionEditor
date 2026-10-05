using System.Collections.Generic;
using UnityEngine;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// 素体ごとに1つ作る共有の表情セット。全バリアントから参照される。
    /// </summary>
    [CreateAssetMenu(menuName = "Samon/表情エディタ/表情セット", fileName = "ExpressionSet")]
    public class ExpressionSet : ScriptableObject
    {
        // 登録フォルダはGUIDで持つ（フォルダを移動・改名しても外れないように）。
        public List<string> libraryFolderGuids = new List<string>();

        public float defaultTransitionDuration = 0.1f;

        public List<Expression> expressions = new List<Expression>();

        public GestureMapping gestures = new GestureMapping();

        public Expression FindExpression(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return expressions.Find(e => e.id == id);
        }
    }
}
