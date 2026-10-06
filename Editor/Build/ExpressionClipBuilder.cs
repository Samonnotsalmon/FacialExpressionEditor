using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情とパーツのビルド用クリップを作る。
    /// </summary>
    internal class ExpressionClipBuilder
    {
        private readonly ExpressionVariant _variant;
        private readonly GameObject _avatarRoot;
        private readonly Dictionary<string, VirtualClip> _cache = new Dictionary<string, VirtualClip>();

        public ExpressionClipBuilder(ExpressionVariant variant, GameObject avatarRoot)
        {
            _variant = variant;
            _avatarRoot = avatarRoot;
        }

        public VirtualClip Build(Expression expression)
        {
            if (_cache.TryGetValue(expression.id, out var cached)) return cached;

            var source = _variant != null ? _variant.ResolveClip(expression) : expression.clip;
            var clip = VirtualClip.Create(expression.name);
            if (source != null)
            {
                clip.Settings = AnimationUtility.GetAnimationClipSettings(source);
                CopyCurves(source, clip, null);
            }

            _cache[expression.id] = clip;
            return clip;
        }

        /// <summary>
        /// パーツのクリップのうち、パーツとして選んだプロパティだけを持つクリップを作る。
        /// </summary>
        public VirtualClip BuildPart(FacialPart part)
        {
            var clip = VirtualClip.Create(part.name);
            if (part.clip != null) CopyCurves(part.clip, clip, new HashSet<string>(part.properties));
            return clip;
        }

        /// <summary>
        /// Write Defaultsがオフのとき用。各クリップで動かしていないプロパティに、アバターの現在値を書き込む。
        /// </summary>
        public void FillMissingWithDefaults(IEnumerable<VirtualClip> clips)
        {
            var list = clips.ToList();
            var floatBindings = list.SelectMany(c => c.GetFloatCurveBindings()).Distinct().ToList();
            var objectBindings = list.SelectMany(c => c.GetObjectCurveBindings()).Distinct().ToList();

            foreach (var clip in list)
            {
                foreach (var binding in floatBindings)
                {
                    if (clip.GetFloatCurve(binding) != null) continue;
                    if (AnimationUtility.GetFloatValue(_avatarRoot, binding, out var value))
                    {
                        clip.SetFloatCurve(binding, new AnimationCurve(new Keyframe(0, value)));
                    }
                }

                foreach (var binding in objectBindings)
                {
                    if (clip.GetObjectCurve(binding) != null) continue;
                    if (AnimationUtility.GetObjectReferenceValue(_avatarRoot, binding, out var value))
                    {
                        clip.SetObjectCurve(binding, new[] { new ObjectReferenceKeyframe { time = 0, value = value } });
                    }
                }
            }
        }

        private static void CopyCurves(AnimationClip from, VirtualClip to, HashSet<string> only)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(from))
            {
                if (only != null && !only.Contains(Key(binding))) continue;
                to.SetFloatCurve(binding, AnimationUtility.GetEditorCurve(from, binding));
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(from))
            {
                if (only != null && !only.Contains(Key(binding))) continue;
                to.SetObjectCurve(binding, AnimationUtility.GetObjectReferenceCurve(from, binding));
            }
        }

        public static string Key(EditorCurveBinding binding)
        {
            return FacialPart.PropertyKey(binding.path, binding.type, binding.propertyName);
        }
    }
}
