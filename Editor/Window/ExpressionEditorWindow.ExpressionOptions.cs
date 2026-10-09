using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private void DrawPlaybackSettings(ExpressionSet set, Expression expression)
        {
            EditorGUILayout.LabelField(PreviewClips.IsTimeVarying(expression.clip) ? "時間変化のある表情" : "静止表情", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            var freeze = EditorGUILayout.ToggleLeft("指定した時点の形で静止する", expression.freezeAnimation);
            var position = freeze ? EditorGUILayout.Slider("静止位置", expression.freezePosition, 0, 1) : expression.freezePosition;
            var grip = EditorGUILayout.ToggleLeft(new GUIContent("Fistでは元のカーブを握り込みに使う", "目閉じ用に作られたクリップだけオン。通常はベース顔から完成表情へ補間します。"), expression.useOriginalGripCurve);
            if (EditorGUI.EndChangeCheck()) Modify(set, "表情の再生方法", () =>
            { expression.freezeAnimation = freeze; expression.freezePosition = position; expression.useOriginalGripCurve = grip; });
        }

    }
}
