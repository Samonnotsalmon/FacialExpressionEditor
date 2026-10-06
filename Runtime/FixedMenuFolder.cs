using System;
using System.Collections.Generic;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// 表情固定メニューのフォルダ（サブメニュー）。ジェスチャーに割り当てていない表情も並べられる。
    /// </summary>
    [Serializable]
    public class FixedMenuFolder
    {
        public string id = Expression.NewId();
        public string name;
        public List<string> expressionIds = new List<string>();
    }
}
