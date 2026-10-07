using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情とパーツのビルド用クリップを作る。表情には顔バリアントの差し替えとベース顔の処理を適用する。
    /// クリップの名前は英数字だけにする（FxNames）。
    /// </summary>
    internal class ExpressionClipBuilder
    {
        private readonly ExpressionSet _set;
        private readonly FaceVariant _variant;
        private readonly GameObject _avatarRoot;
        private readonly Dictionary<string, VirtualClip> _cache = new Dictionary<string, VirtualClip>();

        public ExpressionClipBuilder(ExpressionSet set, FaceVariant variant, GameObject avatarRoot)
        {
            _set = set;
            _variant = variant;
            _avatarRoot = avatarRoot;
        }

        public string NameOf(Expression expression) => FxNames.Expression(_set, expression);

        public VirtualClip Build(Expression expression)
        {
            if (_cache.TryGetValue(expression.id, out var cached)) return cached;

            var overridden = FaceVariantUtility.EffectiveOverride(_variant, expression);
            var source = overridden != null ? overridden.clip : expression.clip;
            var clip = VirtualClip.Create(NameOf(expression));
            if (source != null)
            {
                clip.Settings = AnimationUtility.GetAnimationClipSettings(source);
                CopyCurves(source, clip, null);
            }

            if (_variant != null)
            {
                BaseFaceProcessor.Apply(_variant, expression.id, overridden != null, _avatarRoot,
                    clip.GetFloatCurve, clip.SetFloatCurve);
            }

            _cache[expression.id] = clip;
            return clip;
        }

        /// <summary>
        /// Fistの握り具合で動かすクリップ。握り具合0でベース顔、握り切ると表情になる（FistBlend）。
        /// クリップ自体が時間で動く表情（しなのの目閉じなど）は、表情のクリップをそのまま使う。
        /// </summary>
        public VirtualClip BuildFist(Expression expression)
        {
            var clip = Build(expression);
            if (FistBlend.IsTimeVarying(ClipCurves.Of(clip))) return clip;

            var key = $"{expression.id}|fist";
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var fist = VirtualClip.Create($"{NameOf(expression)} (Grip)");
            var settings = fist.Settings;
            settings.loopTime = false;
            fist.Settings = settings;
            FistBlend.FromBaseFace(ClipCurves.Of(clip), _avatarRoot, ClipCurves.Of(fist));

            _cache[key] = fist;
            return fist;
        }

        /// <summary>
        /// 切り替え演出つきのクリップを作る。頭で挟む表情（目閉じなど）を holdTime の間見せ、fadeTime かけて目的の表情に移る。
        /// 挟む表情が動かさないプロパティは目的の表情の値のまま、目的の表情が動かさないプロパティはアバターの今の値に戻す。
        /// </summary>
        public VirtualClip BuildSwitch(Expression target, Expression between, SwitchEffect effect)
        {
            var targetClip = Build(target);
            var betweenClip = Build(between);
            var hold = Mathf.Max(0, effect.holdTime);
            var end = hold + Mathf.Max(0.0001f, effect.fadeTime);

            var clip = VirtualClip.Create($"{NameOf(target)} (via {NameOf(between)})");
            var settings = clip.Settings;
            settings.loopTime = false;
            clip.Settings = settings;

            var floatBindings = targetClip.GetFloatCurveBindings().Concat(betweenClip.GetFloatCurveBindings()).Distinct();
            foreach (var binding in floatBindings)
            {
                var targetCurve = targetClip.GetFloatCurve(binding);
                var betweenCurve = betweenClip.GetFloatCurve(binding);

                float targetValue;
                if (targetCurve != null) targetValue = EndValue(targetCurve);
                else if (!AnimationUtility.GetFloatValue(_avatarRoot, binding, out targetValue)) continue;
                var betweenValue = betweenCurve != null ? EndValue(betweenCurve) : targetValue;

                var curve = new AnimationCurve(new Keyframe(0, betweenValue), new Keyframe(hold, betweenValue), new Keyframe(end, targetValue));
                for (var i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                }
                clip.SetFloatCurve(binding, curve);
            }

            var objectBindings = targetClip.GetObjectCurveBindings().Concat(betweenClip.GetObjectCurveBindings()).Distinct();
            foreach (var binding in objectBindings)
            {
                var targetKeys = targetClip.GetObjectCurve(binding);
                var betweenKeys = betweenClip.GetObjectCurve(binding);
                Object targetValue;
                if (targetKeys != null && targetKeys.Length > 0) targetValue = targetKeys[targetKeys.Length - 1].value;
                else if (!AnimationUtility.GetObjectReferenceValue(_avatarRoot, binding, out targetValue)) continue;
                var betweenValue = betweenKeys != null && betweenKeys.Length > 0 ? betweenKeys[betweenKeys.Length - 1].value : targetValue;

                clip.SetObjectCurve(binding, new[]
                {
                    new ObjectReferenceKeyframe { time = 0, value = betweenValue },
                    new ObjectReferenceKeyframe { time = hold, value = targetValue },
                });
            }

            return clip;
        }

        private static float EndValue(AnimationCurve curve)
        {
            return curve.length == 0 ? 0 : curve.Evaluate(curve.keys[curve.length - 1].time);
        }

        /// <summary>
        /// パーツのクリップのうち、パーツとして選んだプロパティだけを持つクリップを作る。
        /// </summary>
        public VirtualClip BuildPart(FacialPart part)
        {
            var clip = VirtualClip.Create(FxNames.Part(_set, part));
            if (part.clip != null) CopyCurves(part.clip, clip, new HashSet<string>(part.properties));
            return clip;
        }

        /// <summary>
        /// Write Defaultsがオフのとき用。各クリップで動かしていないプロパティに、アバターの現在値を書き込む。
        /// extraFloatBindings は、どのクリップも動かしていなくても書き込むもの（上のレイヤーが止まっている間に値を戻すため）。
        /// </summary>
        public void FillMissingWithDefaults(IEnumerable<VirtualClip> clips, IEnumerable<EditorCurveBinding> extraFloatBindings = null)
        {
            var list = clips.ToList();
            var floatBindings = list.SelectMany(c => c.GetFloatCurveBindings())
                .Concat(extraFloatBindings ?? Enumerable.Empty<EditorCurveBinding>())
                .Distinct()
                .ToList();
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
