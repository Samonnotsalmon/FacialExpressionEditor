using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class PreviewNavigation
    {
        public const string Hint="右ドラッグ：回転  中ドラッグ：移動  ホイール：ズーム";
        public static Rect Square(float maximum=320)
        {
            var area=GUILayoutUtility.GetRect(0,maximum,GUILayout.ExpandWidth(true));
            float size=Mathf.Min(maximum,area.width);
            return new Rect(area.x+(area.width-size)*.5f,area.y,size,size);
        }
        public static bool Handle(Rect rect,ref Vector3 offset,ref float yaw,ref float pitch,ref float zoom)
        {
            int id=GUIUtility.GetControlID("FacePreviewNavigation".GetHashCode(),FocusType.Passive,rect);
            var e=Event.current;
            if(e.type==EventType.MouseDown&&rect.Contains(e.mousePosition)&&(e.button==1||e.button==2)){
                GUIUtility.hotControl=id;e.Use();return false;
            }
            if(e.type==EventType.MouseUp&&GUIUtility.hotControl==id){GUIUtility.hotControl=0;e.Use();return false;}
            if(e.type==EventType.MouseDrag&&GUIUtility.hotControl==id){
                if(e.button==1){yaw=Mathf.Repeat(yaw+e.delta.x*.5f+180,360)-180;pitch=Mathf.Clamp(pitch-e.delta.y*.5f,-80,80);}
                else if(e.button==2)offset+=Quaternion.Euler(pitch,yaw,0)*new Vector3(e.delta.x,e.delta.y,0)*(.5f/Mathf.Max(.2f,zoom)/Mathf.Max(1,rect.height));
                e.Use();GUI.changed=true;return true;
            }
            if(e.type==EventType.ScrollWheel&&rect.Contains(e.mousePosition)){
                zoom=Mathf.Clamp(zoom*Mathf.Exp(-e.delta.y*.07f),.2f,8);e.Use();GUI.changed=true;return true;
            }
            if(e.type==EventType.ContextClick&&rect.Contains(e.mousePosition))e.Use();
            return false;
        }
    }
}
