using System;

namespace Samon.FacialExpressionEditor
{
    public enum MenuNodeKind
    {
        // サブメニュー。
        Folder,
        // 表情セット（ジェスチャーで表情が変わるモード）。
        GestureSet,
        // 固定の表情（選ぶとその表情に固定されるモード）。
        Expression,
    }

    /// <summary>
    /// 表情メニューの1項目。FaceEmoと同じく、表情セットと固定の表情はどちらも「モード」で、
    /// メニューで選んだモードが1つだけ有効になる。木構造は parentId で表し、並び順はリストの順。
    /// </summary>
    [Serializable]
    public class MenuNode
    {
        public string id = Expression.NewId();

        // 親フォルダの id。空ならメニューの一番上。
        public string parentId = "";

        public MenuNodeKind kind;

        // フォルダ名（表情セットと表情は、それぞれの名前を使う）。
        public string name;

        public string gestureSetId;
        public string expressionId;
    }
}
