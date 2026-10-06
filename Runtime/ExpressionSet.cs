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

        public List<GestureSet> gestureSets = new List<GestureSet>();

        public List<FacialPart> parts = new List<FacialPart>();

        // 元FXのジェスチャーレイヤー名。ビルド時に取り除き、その位置に生成した表情レイヤーを入れる。
        public List<string> originalGestureLayers = new List<string>();

        // 元FXのパーツ用レイヤー名。ビルド時に取り除き、その位置に生成したパーツレイヤーを入れる。
        public List<string> originalPartLayers = new List<string>();

        public Expression FindExpression(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return expressions.Find(e => e.id == id);
        }

        public float GetTransitionDuration(Expression expression)
        {
            return expression != null && expression.overrideTransitionDuration
                ? expression.transitionDuration
                : defaultTransitionDuration;
        }

        private void OnValidate()
        {
            foreach (var gestureSet in gestureSets) gestureSet.mapping.EnsureSize();
        }
    }
}
