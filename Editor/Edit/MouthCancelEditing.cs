using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class MouthCancelEditing
    {
        internal static string BaseFaceHighlight(FaceVariant variant, string path, string name)
        {
            var key = variant != null ? variant.baseFace.Find(k => k.enabled && k.path == path && k.blendShape == name) : null;
            if (key == null || Mathf.Abs(key.variantValue - key.referenceValue) < 0.01f) return null;
            return $"ベース顔で変更：{key.referenceValue:0.##} → {key.variantValue:0.##}\n{path}";
        }

        internal static void SetShapeSelected(ExpressionSet set, GameObject root, EditorCurveBinding binding, bool selected)
        {
            if (LipSyncProtection.IsProtected(binding, root, set) || !AnimationUtility.GetFloatValue(root, binding, out _)) return;
            var clip = ExpressionSetUtility.MakeMouthCancelEditable(set);
            if (clip == null) return;
            if (selected)
            {
                if (AnimationUtility.GetEditorCurve(clip, binding) == null) SetValue(clip, binding, 0);
            }
            else
            {
                Undo.RecordObject(clip, "口キャンセルの対象を外す");
                AnimationUtility.SetEditorCurve(clip, binding, null);
                EditorUtility.SetDirty(clip);
            }
            AssetDatabase.SaveAssetIfDirty(clip);
        }

        internal static void SetValue(AnimationClip clip, EditorCurveBinding binding, float value)
        {
            Undo.RecordObject(clip, "口キャンセルの値を変更");
            var curve = AnimationUtility.GetEditorCurve(clip, binding) ?? new AnimationCurve();
            // Keep the source animation's later keys; runtime samples only time zero.
            var index = System.Array.FindIndex(curve.keys, k => k.time == 0);
            if (index >= 0) { var key = curve.keys[index]; key.value = value; curve.MoveKey(index, key); }
            else curve.AddKey(new Keyframe(0, value));
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            EditorUtility.SetDirty(clip);
        }

    }
}
