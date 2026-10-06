using System;
using UnityEngine;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// ジェスチャーの割り当て1組（UI上の「表情セット」。しなのの F_Set の1セット分など）。
    /// 表情メニューに置くと、メニューで選べるモードになる。
    /// </summary>
    [Serializable]
    public class GestureSet
    {
        public string id = Expression.NewId();
        public string name;

        public GestureMapping mapping = new GestureMapping();

        // 旧形式の設定。表情メニューへ移すときにだけ使う。
        [HideInInspector] public bool useForGesture = true;
    }
}
