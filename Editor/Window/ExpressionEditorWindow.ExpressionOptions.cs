using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private void DrawPlaybackSettings(ExpressionSet set, Expression expression)
        {
            var moving = PreviewClips.IsTimeVarying(expression.clip);
            EditorGUI.BeginChangeCheck();
            var freeze = moving || expression.freezeAnimation
                ? EditorGUILayout.ToggleLeft("指定した時点で静止する", expression.freezeAnimation) : expression.freezeAnimation;
            var position = freeze ? EditorGUILayout.Slider("静止位置", expression.freezePosition, 0, 1) : expression.freezePosition;
            var grip = _selectionKind == SelectionKind.Fist || expression.useOriginalGripCurve ? EditorGUILayout.ToggleLeft(new GUIContent("Fistでは元のカーブを握り込みに使う", "目閉じ用に作られたクリップだけオン。通常はベース顔から完成表情へ補間します。"), expression.useOriginalGripCurve) : expression.useOriginalGripCurve;
            if (EditorGUI.EndChangeCheck()) Modify(set, "表情の再生方法", () =>
            { expression.freezeAnimation = freeze; expression.freezePosition = position; expression.useOriginalGripCurve = grip; });
        }

    }
}
