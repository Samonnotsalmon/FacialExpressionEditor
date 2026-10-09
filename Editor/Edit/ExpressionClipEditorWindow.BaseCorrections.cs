using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionClipEditorWindow
    {
        private void DrawBaseFaceExclusions(Expression expression)
        {
            if (IsAfk || IsPart) return;
            var baseline = Variant;
            if (baseline == null || baseline.baseFace.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ベース顔の補正", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("外した項目は元の表情の値・動きを使います。", EditorStyles.wordWrappedMiniLabel);
            if (FaceVariantUtility.EffectiveOverride(baseline, expression) != null)
            {
                EditorGUILayout.HelpBox("旧形式の差し替えにベース顔が書き込み済みです。補正の除外は元クリップから作成した表情で行ってください。", MessageType.Info);
                return;
            }
            var exclusion = baseline.baseFaceExclusions.Find(e => e.expressionId == expression.id);
            foreach (var key in baseline.baseFace.Where(k => k.enabled))
            {
                var enabled = exclusion == null || !exclusion.keys.Contains(key.Key);
                var next = EditorGUILayout.ToggleLeft(new GUIContent(key.blendShape, key.path), enabled);
                if (next == enabled) continue;
                Undo.RecordObject(baseline, "この表情のベース顔補正");
                if (exclusion == null) { exclusion = new BaseFaceExclusion { expressionId = expression.id }; baseline.baseFaceExclusions.Add(exclusion); }
                if (next) exclusion.keys.Remove(key.Key); else exclusion.keys.Add(key.Key);
                // 旧形式の最終値上書きよりも、元カーブの保持を優先する。
                baseline.FindFaceValues(expression.id)?.values.RemoveAll(v => v.path == key.path && v.blendShape == key.blendShape);
                EditorUtility.SetDirty(baseline);
                AfterEdit();
            }
        }
    }
}
