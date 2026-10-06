using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情とパーツのビルド用クリップを作る。表情には顔バリアントの差し替えとベース顔の処理を適用する。
    /// </summary>
    internal class ExpressionClipBuilder
    {
        private const float BlendShapeMin = 0;
        private const float BlendShapeMax = 100;

        private readonly FaceVariant _variant;
        private readonly GameObject _avatarRoot;
        private readonly Dictionary<string, VirtualClip> _cache = new Dictionary<string, VirtualClip>();

        public ExpressionClipBuilder(FaceVariant variant, GameObject avatarRoot)
        {
            _variant = variant;
            _avatarRoot = avatarRoot;
        }

        public VirtualClip Build(Expression expression)
        {
            if (_cache.TryGetValue(expression.id, out var cached)) return cached;

            var overridden = _variant != null ? _variant.FindOverride(expression.id) : null;
            var source = overridden != null ? overridden.clip : expression.clip;
            var clip = VirtualClip.Create(expression.name);
            if (source != null)
            {
                clip.Settings = AnimationUtility.GetAnimationClipSettings(source);
                CopyCurves(source, clip, null);
            }

            if (_variant != null) ApplyBaseFace(clip, _variant.ShouldKeepBaseFace(expression.id), overridden != null);

            _cache[expression.id] = clip;
            return clip;
        }

        /// <summary>
        /// ベース顔のシェイプキーを処理する。「常に残す」のシェイプキーは、表情の設定に関係なく残す。
        /// - リセット：クリップが動かしていなければ元Prefabの値にする（動かしていればクリップの値のまま）
        /// - 残す：クリップの値に「バリアントの値 − 元Prefabの値」を足す。動かしていなければバリアントの値にする
        /// 差し替えクリップはそのバリアント用に作ったものなので、差分は足さずにクリップの値をそのまま使う。
        /// </summary>
        private void ApplyBaseFace(VirtualClip clip, bool keepExpression, bool overridden)
        {
            foreach (var key in _variant.baseFace)
            {
                if (!key.enabled) continue;
                var keepBaseFace = keepExpression || key.alwaysKeep;

                var renderer = FindRenderer(key.path);
                var index = renderer != null ? renderer.sharedMesh.GetBlendShapeIndex(key.blendShape) : -1;
                if (index < 0) continue;

                var current = renderer.GetBlendShapeWeight(index);
                var binding = EditorCurveBinding.FloatCurve(key.path, typeof(SkinnedMeshRenderer), "blendShape." + key.blendShape);
                var curve = clip.GetFloatCurve(binding);

                if (curve == null)
                {
                    var value = keepBaseFace ? current : key.referenceValue;
                    clip.SetFloatCurve(binding, new AnimationCurve(new Keyframe(0, value)));
                }
                else if (keepBaseFace && !overridden)
                {
                    clip.SetFloatCurve(binding, Offset(curve, current - key.referenceValue));
                }
            }
        }

        private SkinnedMeshRenderer FindRenderer(string path)
        {
            var transform = string.IsNullOrEmpty(path) ? _avatarRoot.transform : _avatarRoot.transform.Find(path);
            var renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
            return renderer != null && renderer.sharedMesh != null ? renderer : null;
        }

        private static AnimationCurve Offset(AnimationCurve curve, float delta)
        {
            var keys = curve.keys;
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i].value = Mathf.Clamp(keys[i].value + delta, BlendShapeMin, BlendShapeMax);
            }
            return new AnimationCurve(keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
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
