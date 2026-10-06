using System;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// ジェスチャーの割り当て1組（しなのの F_Set の1セット分など）。
    /// </summary>
    [Serializable]
    public class GestureSet
    {
        public string id = Expression.NewId();
        public string name;

        // ジェスチャーで表情を出すセットとして使う（2つ以上あればメニューで切り替える）。
        public bool useForGesture = true;

        // このセットの表情を、表情固定メニューに並べる。
        public bool showInFixedMenu = true;

        public GestureMapping mapping = new GestureMapping();
    }
}
