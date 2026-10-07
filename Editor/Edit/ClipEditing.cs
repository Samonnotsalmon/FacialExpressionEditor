using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情クリップの値を読み書きする（表情の編集ウィンドウ用）。値は最後のキーの値（表情として見える形）。
    /// 時間で動くカーブ（握り具合で動く目閉じなど）は最後のキーだけを変え、それ以外は全部のキーを同じ値にする。
    /// </summary>
    internal static class ClipEditing
    {
        public const string BlendShapePrefix = "blendShape.";
        public const string MaterialPrefix = "material.";
        public const string IsActive = "m_IsActive";

        public static EditorCurveBinding BlendShape(string path, string name)
        {
            return EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), BlendShapePrefix + name);
        }

        public static EditorCurveBinding Active(string path)
        {
            return EditorCurveBinding.FloatCurve(path, typeof(GameObject), IsActive);
        }

        public static EditorCurveBinding MaterialSlot(string path, System.Type rendererType, int slot)
        {
            return EditorCurveBinding.PPtrCurve(path, rendererType, $"m_Materials.Array.data[{slot}]");
        }

        public static bool TryGetFloat(AnimationClip clip, EditorCurveBinding binding, out float value)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length == 0)
            {
                value = 0;
                return false;
            }
            value = curve.keys[curve.length - 1].value;
            return true;
        }

        public static void SetFloat(AnimationClip clip, EditorCurveBinding binding, float value)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length == 0)
            {
                curve = clip.length > 0
                    ? new AnimationCurve(new Keyframe(0, value), new Keyframe(clip.length, value))
                    : new AnimationCurve(new Keyframe(0, value));
            }
            else
            {
                var keys = curve.keys;
                var animated = keys.Any(k => !Mathf.Approximately(k.value, keys[0].value));
                if (animated)
                {
                    keys[keys.Length - 1].value = value;
                }
                else
                {
                    for (var i = 0; i < keys.Length; i++) keys[i].value = value;
                }
                curve.keys = keys;
            }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        public static void RemoveFloat(AnimationClip clip, EditorCurveBinding binding)
        {
            AnimationUtility.SetEditorCurve(clip, binding, null);
        }

        public static Object GetObject(AnimationClip clip, EditorCurveBinding binding)
        {
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            return keys != null && keys.Length > 0 ? keys[keys.Length - 1].value : null;
        }

        public static void SetObject(AnimationClip clip, EditorCurveBinding binding, Object value)
        {
            var keys = clip.length > 0
                ? new[] { new ObjectReferenceKeyframe { time = 0, value = value }, new ObjectReferenceKeyframe { time = clip.length, value = value } }
                : new[] { new ObjectReferenceKeyframe { time = 0, value = value } };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        }

        public static void RemoveObject(AnimationClip clip, EditorCurveBinding binding)
        {
            AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
        }

        /// <summary>
        /// target のカーブを source と同じにする（「開いたときの状態に戻す」用）。
        /// </summary>
        public static void CopyCurves(AnimationClip source, AnimationClip target)
        {
            target.ClearCurves();
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(target))
            {
                AnimationUtility.SetObjectReferenceCurve(target, binding, null);
            }

            var floatBindings = AnimationUtility.GetCurveBindings(source);
            AnimationUtility.SetEditorCurves(target, floatBindings, floatBindings.Select(b => AnimationUtility.GetEditorCurve(source, b)).ToArray());
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                AnimationUtility.SetObjectReferenceCurve(target, binding, AnimationUtility.GetObjectReferenceCurve(source, binding));
            }
        }
    }
}
