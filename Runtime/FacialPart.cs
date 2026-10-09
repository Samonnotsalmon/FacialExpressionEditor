using System;
using System.Collections.Generic;
using UnityEngine;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// 汗・涙・頬染めなど、ゲーム中にメニューから表情へ重ねて出すパーツ。
    /// </summary>
    [Serializable]
    public class FacialPart
    {
        public string id = Expression.NewId();
        public string name;
        public AnimationClip clip;
        public AnimationClip originalClip;

        // 同じグループのパーツは同時に出ない（1つのIntパラメータで切り替える）。空なら単独のトグル。
        public string exclusiveGroup = "";

        // clip のうち、このパーツとして動かすプロパティ（PropertyKey 形式）。
        public List<string> properties = new List<string>();

        /// <summary>
        /// カーブのバインディングを識別するキー。
        /// </summary>
        public static string PropertyKey(string path, Type type, string propertyName)
        {
            return $"{path}|{type?.FullName}|{propertyName}";
        }
    }
}
