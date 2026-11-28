using System;
using UnityEngine;

namespace Samon.FacialExpressionEditor
{
    public enum MenuIconKind { Root, Sets, Selection, Parts, EyeLook, Settings, Reset, BlinkOff, Next, Folder, Check, Dance, Power, Base }

    [Serializable]
    public class MenuIconOverride
    {
        public MenuIconKind kind;
        public Texture2D texture;
    }
}
