using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// AnimationClip と VirtualClip のカーブを同じ形で読み書きする（ビルドとプレビューで同じ処理を使うため）。
    /// </summary>
    internal sealed class ClipCurves
    {
        public Func<IEnumerable<EditorCurveBinding>> FloatBindings;
        public Func<IEnumerable<EditorCurveBinding>> ObjectBindings;
        public Func<EditorCurveBinding, AnimationCurve> GetFloat;
        public Func<EditorCurveBinding, ObjectReferenceKeyframe[]> GetObject;
        public Action<EditorCurveBinding, AnimationCurve> SetFloat;
        public Action<EditorCurveBinding, ObjectReferenceKeyframe[]> SetObject;

        public static ClipCurves Of(AnimationClip clip) => new ClipCurves
        {
            FloatBindings = () => AnimationUtility.GetCurveBindings(clip),
            ObjectBindings = () => AnimationUtility.GetObjectReferenceCurveBindings(clip),
            GetFloat = b => AnimationUtility.GetEditorCurve(clip, b),
            GetObject = b => AnimationUtility.GetObjectReferenceCurve(clip, b),
            SetFloat = (b, c) => AnimationUtility.SetEditorCurve(clip, b, c),
            SetObject = (b, k) => AnimationUtility.SetObjectReferenceCurve(clip, b, k),
        };

        public static ClipCurves Of(VirtualClip clip) => new ClipCurves
        {
            FloatBindings = clip.GetFloatCurveBindings,
            ObjectBindings = clip.GetObjectCurveBindings,
            GetFloat = clip.GetFloatCurve,
            GetObject = clip.GetObjectCurve,
            SetFloat = clip.SetFloatCurve,
            SetObject = clip.SetObjectCurve,
        };
    }

    /// <summary>
    /// Fistの握り具合で動かす表情のクリップ。握り具合0でベース顔（アバターの今の値）、握り切ると表情（最後まで再生した形）になる、
    /// 長さ1秒のクリップを作る。ステートのモーションタイムに握り具合を入れて使う（ブレンドツリーを使わず、ステートも増やさないため）。
    /// クリップ自体が時間で動く表情（しなのの目閉じのように、握り具合で動かす前提で作られたもの）は、作らずにそのまま使う。
    /// </summary>
    internal static class FistBlend
    {
        public const float Length = 1f;

        // オブジェクトの切り替えやマテリアルの差し替えは、半分以上握ったところで切り替える。
        private const float ObjectSwitchTime = 0.5f;

        public static bool IsTimeVarying(ClipCurves clip)
        {
            return clip.FloatBindings()
                .Select(clip.GetFloat)
                .Any(c => c != null && c.length > 1 && c.keys.Any(k => !Mathf.Approximately(k.value, c.keys[0].value)));
        }

        public static void FromBaseFace(ClipCurves expression, GameObject avatarRoot, ClipCurves output)
        {
            foreach (var binding in expression.FloatBindings().ToList())
            {
                var curve = expression.GetFloat(binding);
                if (curve == null || curve.length == 0) continue;

                var end = curve.Evaluate(curve.keys[curve.length - 1].time);
                if (!AnimationUtility.GetFloatValue(avatarRoot, binding, out var start)) start = end;

                var blend = new AnimationCurve(new Keyframe(0, start), new Keyframe(Length, end));
                for (var i = 0; i < blend.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(blend, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(blend, i, AnimationUtility.TangentMode.Linear);
                }
                output.SetFloat(binding, blend);
            }

            foreach (var binding in expression.ObjectBindings().ToList())
            {
                var keys = expression.GetObject(binding);
                if (keys == null || keys.Length == 0) continue;

                var end = keys[keys.Length - 1].value;
                if (!AnimationUtility.GetObjectReferenceValue(avatarRoot, binding, out Object start)) start = end;

                output.SetObject(binding, new[]
                {
                    new ObjectReferenceKeyframe { time = 0, value = start },
                    new ObjectReferenceKeyframe { time = ObjectSwitchTime, value = end },
                    new ObjectReferenceKeyframe { time = Length, value = end },
                });
            }
        }
    }
}
