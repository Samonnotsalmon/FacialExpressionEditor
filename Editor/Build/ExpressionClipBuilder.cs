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

        public VirtualClip BuildBaseFace()
        {
            var clip = VirtualClip.Create("Base Face (Fixed)");
            var source = PreviewClips.ForBaseFace(_set, _variant, _avatarRoot);
            try { CopyCurves(source, clip, null); }
            finally { PreviewClips.Release(source); }
            return clip;
        }

        public VirtualClip Build(Expression expression)
        {
            if (_cache.TryGetValue(expression.id, out var cached)) return cached;

            var source = PreviewClips.ForExpression(expression, _variant, _avatarRoot);
            var clip = VirtualClip.Create(NameOf(expression));
            if (source != null)
            {
                clip.Settings = AnimationUtility.GetAnimationClipSettings(source);
                CopyCurves(source, clip, null);
            }

            PreviewClips.Release(source);

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

            var key = $"{expression.id}|fist";
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var fist = VirtualClip.Create($"{NameOf(expression)} (Grip)");
            var settings = fist.Settings;
            settings.loopTime = false;
            fist.Settings = settings;
            FistBlend.FromBaseFace(ClipCurves.Of(clip), _avatarRoot, ClipCurves.Of(fist), _set, expression.useOriginalGripCurve && !expression.freezeAnimation, _variant);

            _cache[key] = fist;
            return fist;
        }

        /// <summary>
        /// 切り替え演出つきのクリップを作る。頭で挟む表情（目閉じなど）を holdTime の間見せ、fadeTime かけて目的の表情に移る。
        /// 挟む表情が動かさないプロパティは目的の表情の値のまま、目的の表情が動かさないプロパティはアバターの今の値に戻す。
        /// </summary>
        public VirtualClip BuildBlinkSwitch(Expression target, FaceControlPlan face, SwitchEffect effect)
        {
            var pose = new AnimationClip();
            try
            {
                // Sample a single shared closing pose, excluding idle time in looping blink clips.
                var peakTime = 0f;
                var largestChange = -1f;
                if (face.BlinkClip != null)
                    foreach (var (binding, _) in face.BlinkShapes)
                    {
                        var curve = AnimationUtility.GetEditorCurve(face.BlinkClip, binding);
                        if (curve == null) continue;
                        var open = curve.Evaluate(0);
                        foreach (var key in curve.keys)
                            if (Mathf.Abs(key.value - open) > largestChange)
                            { largestChange = Mathf.Abs(key.value - open); peakTime = key.time; }
                    }
                foreach (var (binding, closed) in face.BlinkShapes)
                {
                    var curve = face.BlinkClip != null ? AnimationUtility.GetEditorCurve(face.BlinkClip, binding) : null;
                    AnimationUtility.SetEditorCurve(pose, binding, AnimationCurve.Constant(0, 0, curve != null ? curve.Evaluate(peakTime) : closed));
                }
                return BuildSwitch(target, new Expression { name = "共通の瞬き", clip = pose, baseFaceBaked = true }, effect);
            }
            finally { Object.DestroyImmediate(pose); }
        }

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
            if (part.clip != null)
            {
                clip.Settings = AnimationUtility.GetAnimationClipSettings(part.clip);
                CopyCurves(part.clip, clip, new HashSet<string>(part.properties));
            }
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
                    // マテリアルの値は、メッシュの先頭のマテリアルの値ではなく、戻すべき値を選ぶ（下の TryGetMaterialRestValue）。
                    var found = IsMaterialBinding(binding)
                        ? TryGetMaterialRestValue(binding, out var value)
                        : AnimationUtility.GetFloatValue(_avatarRoot, binding, out value);
                    if (found) clip.SetFloatCurve(binding, new AnimationCurve(new Keyframe(0, value)));
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

        private readonly Dictionary<EditorCurveBinding, float> _materialDefaults = new Dictionary<EditorCurveBinding, float>();

        public void ReadMaterialDefaults(IEnumerable<VirtualClip> clips)
        {
            foreach (var clip in clips)
            foreach (var binding in clip.GetFloatCurveBindings())
            {
                if (!binding.propertyName.StartsWith("material.")) continue;
                var curve = clip.GetFloatCurve(binding);
                if (curve == null || curve.length == 0) continue;
                // A static default pose is authoritative when different material slots
                // expose the same property with different asset defaults.
                var value = curve.Evaluate(0);
                if (curve.keys.All(k => Mathf.Approximately(k.value, value))) _materialDefaults[binding] = value;
            }
        }

        // Material animation needs explicit reset curves even with Write Defaults ON.
        public void FillMissingMaterialDefaults(IEnumerable<VirtualClip> clips)
        {
            var list = clips.ToList();
            var bindings = list.SelectMany(c => c.GetFloatCurveBindings()).Where(IsMaterialBinding).Distinct().ToList();
            foreach (var binding in bindings)
            {
                if (!TryGetMaterialRestValue(binding, out var value)) continue;
                foreach (var clip in list.Where(c => c.GetFloatCurve(binding) == null))
                    clip.SetFloatCurve(binding, AnimationCurve.Constant(0, 1, value));
            }
        }

        private static bool IsMaterialBinding(EditorCurveBinding binding) =>
            typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith("material.");

        private readonly Dictionary<Material, Dictionary<string, float>> _materialValues = new Dictionary<Material, Dictionary<string, float>>();

        // マテリアルごとに初期値が違い、戻す値を決められなかったパラメータ（ビルドの警告に出す）。
        public readonly HashSet<string> AmbiguousMaterialProperties = new HashSet<string>();

        /// <summary>
        /// 表情やパーツが記録していないときに戻すマテリアルの値。アニメーションのマテリアルの値はメッシュの全マテリアルに
        /// 同じ値で入るので（Write Defaults で戻る値も先頭のマテリアルのもの）、マテリアルごとに初期値が違うと、
        /// 先頭のマテリアルの値を使うと別のマテリアル（顔のほほ染めなど）が変わってしまう。次の順に決める。
        /// 1. 元FXの待機中のステートが書いている値（作者が決めた「戻す値」）
        /// 2. そのパラメータを持つマテリアルがすべて同じ値なら、その値
        /// 3. 表情の編集で記録したときに選んでいたマテリアルの値
        /// 4. 決まらなければ先頭のマテリアルの値（Unity と同じ）。ビルドの警告に出す
        /// </summary>
        private bool TryGetMaterialRestValue(EditorCurveBinding binding, out float value)
        {
            if (_materialDefaults.TryGetValue(binding, out value)) return true;

            value = 0;
            var transform = string.IsNullOrEmpty(binding.path) ? _avatarRoot.transform : _avatarRoot.transform.Find(binding.path);
            var renderer = transform != null ? transform.GetComponent(binding.type) as Renderer : null;
            if (renderer == null) return false;

            var property = binding.propertyName.Substring("material.".Length);
            var candidates = new List<(int slot, float value)>();
            var materials = renderer.sharedMaterials;
            for (var slot = 0; slot < materials.Length; slot++)
            {
                var material = materials[slot];
                if (material == null) continue;
                if (!_materialValues.TryGetValue(material, out var values))
                    _materialValues[material] = values = MaterialAnimationValues.Read(material);
                if (values.TryGetValue(property, out var v)) candidates.Add((slot, v));
            }
            if (candidates.Count == 0) return false;

            var first = candidates[0].value;
            value = first;
            if (candidates.All(c => Mathf.Approximately(c.value, first))) return true;

            var dot = property.LastIndexOf('.');
            var baseName = dot >= 0 ? property.Substring(0, dot) : property;
            var source = _set.materialPropertySources.Find(s => s.path == binding.path && s.property == baseName);
            var chosen = source != null ? candidates.FindIndex(c => c.slot == source.slot) : -1;
            if (chosen >= 0)
            {
                value = candidates[chosen].value;
                return true;
            }

            AmbiguousMaterialProperties.Add($"{binding.path}：{property}");
            return true;
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
