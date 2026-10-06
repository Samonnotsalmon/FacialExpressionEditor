using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 顔バリアントの作成、ベース顔の検出、表情の差し替えを行う。
    /// </summary>
    public static class FaceVariantUtility
    {
        private const string BlendShapePrefix = "blendShape.";
        private const float Epsilon = 0.01f;

        /// <summary>
        /// 表情データのフォルダの Variants/&lt;名前&gt;/ に顔バリアントを作り、アバターからベース顔を検出する。
        /// </summary>
        public static FaceVariant Create(ExpressionSet set, GameObject avatarRoot, string name)
        {
            var fileName = AssetPathUtility.SafeFileName(name, "Variant");
            var folder = AssetPathUtility.EnsureFolder(AssetPathUtility.FolderOf(set), "Variants", fileName);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.asset");

            var variant = ScriptableObject.CreateInstance<FaceVariant>();
            AssetDatabase.CreateAsset(variant, path);
            DetectBaseFace(variant, set, avatarRoot);
            AssetDatabase.SaveAssets();
            return variant;
        }

        /// <summary>
        /// 元Prefabと値が違うシェイプキーを、ベース顔の候補として検出する。
        /// 表情データのクリップが動かすシェイプキーは有効、それ以外は無効の状態で追加する。
        /// 既に登録済みのシェイプキーは、有効・無効と「常に残す」の設定を引き継ぐ。戻り値は、元Prefabが見つからなかったレンダラーのパス。
        /// </summary>
        public static List<string> DetectBaseFace(FaceVariant variant, ExpressionSet set, GameObject avatarRoot)
        {
            var missingReference = new List<string>();
            var animated = AnimatedBlendShapes(set);
            var previous = variant.baseFace
                .GroupBy(k => (k.path, k.blendShape))
                .ToDictionary(g => g.Key, g => g.First());
            var detected = new List<BaseFaceKey>();

            foreach (var path in animated.Select(a => a.path).Distinct())
            {
                var transform = string.IsNullOrEmpty(path) ? avatarRoot.transform : avatarRoot.transform.Find(path);
                var renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
                if (renderer == null || renderer.sharedMesh == null) continue;

                var reference = PrefabUtility.GetCorrespondingObjectFromOriginalSource(renderer);
                if (reference == null || reference.sharedMesh == null)
                {
                    missingReference.Add(path);
                    continue;
                }

                var mesh = renderer.sharedMesh;
                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    var name = mesh.GetBlendShapeName(i);
                    var referenceIndex = reference.sharedMesh.GetBlendShapeIndex(name);
                    var referenceValue = referenceIndex >= 0 ? reference.GetBlendShapeWeight(referenceIndex) : 0;
                    var value = renderer.GetBlendShapeWeight(i);
                    if (Mathf.Abs(value - referenceValue) < Epsilon) continue;

                    detected.Add(new BaseFaceKey
                    {
                        path = path,
                        blendShape = name,
                        referenceValue = referenceValue,
                        variantValue = value,
                        enabled = previous.TryGetValue((path, name), out var old)
                            ? old.enabled
                            : animated.Contains((path, name)),
                        alwaysKeep = old != null && old.alwaysKeep,
                    });
                }
            }

            Undo.RecordObject(variant, "ベース顔を検出");
            variant.baseFace = detected;
            EditorUtility.SetDirty(variant);
            return missingReference;
        }

        /// <summary>
        /// ベース顔のうち、このアバターでの値が検出したときと違うもの（別の顔のアバターで使っている可能性がある）。
        /// </summary>
        public static List<BaseFaceKey> Mismatched(FaceVariant variant, GameObject avatarRoot)
        {
            var result = new List<BaseFaceKey>();
            foreach (var key in variant.baseFace.Where(k => k.enabled))
            {
                var transform = string.IsNullOrEmpty(key.path) ? avatarRoot.transform : avatarRoot.transform.Find(key.path);
                var renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
                var index = renderer != null && renderer.sharedMesh != null ? renderer.sharedMesh.GetBlendShapeIndex(key.blendShape) : -1;
                if (index < 0 || Mathf.Abs(renderer.GetBlendShapeWeight(index) - key.variantValue) >= Epsilon) result.Add(key);
            }
            return result;
        }

        /// <summary>
        /// 共有の表情クリップをバリアントのフォルダに複製し、このバリアントの差し替えにする。
        /// 複製したクリップには、この顔で見えるとおりにベース顔を反映しておく（そこから編集を始められるように）。
        /// </summary>
        public static AnimationClip DuplicateForVariant(FaceVariant variant, Expression expression)
        {
            if (expression.clip == null) return null;

            var sourcePath = AssetDatabase.GetAssetPath(expression.clip);
            var folder = AssetPathUtility.FolderOf(variant);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{AssetPathUtility.SafeFileName(expression.clip.name)}.anim");
            if (!AssetDatabase.CopyAsset(sourcePath, path)) return null;

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Undo.RecordObject(variant, "表情を差し替え");
            variant.overrides.RemoveAll(o => o.expressionId == expression.id);
            var entry = new ExpressionOverride
            {
                expressionId = expression.id,
                clip = clip,
                sourceHash = SourceHash(expression.clip),
            };
            variant.overrides.Add(entry);
            BakeBaseFace(variant, entry);
            EditorUtility.SetDirty(variant);
            return clip;
        }

        /// <summary>
        /// 差し替えクリップに、残すベース顔の差分（バリアントの値 − 元Prefabの値）を書き込む。
        /// 反映済みのものには何もしない。戻り値は反映したかどうか。
        /// </summary>
        public static bool BakeBaseFace(FaceVariant variant, ExpressionOverride entry)
        {
            if (entry.baseFaceBaked || entry.clip == null) return false;

            var keepExpression = variant.ShouldKeepBaseFace(entry.expressionId);
            Undo.RecordObject(entry.clip, "ベース顔を反映");
            foreach (var key in variant.baseFace)
            {
                if (!key.enabled || !(keepExpression || key.alwaysKeep)) continue;

                var binding = EditorCurveBinding.FloatCurve(key.path, typeof(SkinnedMeshRenderer), BlendShapePrefix + key.blendShape);
                var curve = AnimationUtility.GetEditorCurve(entry.clip, binding);
                if (curve == null)
                {
                    AnimationUtility.SetEditorCurve(entry.clip, binding, new AnimationCurve(new Keyframe(0, key.variantValue)));
                    continue;
                }

                var keys = curve.keys;
                for (var i = 0; i < keys.Length; i++)
                {
                    keys[i].value = Mathf.Clamp(keys[i].value + key.variantValue - key.referenceValue, 0, 100);
                }
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(entry.clip, binding, curve);
            }

            Undo.RecordObject(variant, "ベース顔を反映");
            entry.baseFaceBaked = true;
            EditorUtility.SetDirty(entry.clip);
            EditorUtility.SetDirty(variant);
            return true;
        }

        /// <summary>
        /// 差し替えをやめて共有の表情に戻す。複製したクリップのファイルは残す。
        /// </summary>
        public static void RevertOverride(FaceVariant variant, string expressionId)
        {
            Undo.RecordObject(variant, "共有に戻す");
            variant.overrides.RemoveAll(o => o.expressionId == expressionId);
            EditorUtility.SetDirty(variant);
        }

        /// <summary>
        /// 複製した後に、共有の表情クリップが更新されたかどうか。
        /// </summary>
        public static bool IsSourceUpdated(ExpressionOverride entry, Expression expression)
        {
            return expression.clip != null && !string.IsNullOrEmpty(entry.sourceHash) &&
                   entry.sourceHash != SourceHash(expression.clip);
        }

        private static string SourceHash(AnimationClip clip)
        {
            return AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(clip)).ToString();
        }

        /// <summary>
        /// 表情データのクリップが動かすシェイプキー（アバターのルートからのパスと名前）。
        /// </summary>
        internal static HashSet<(string path, string blendShape)> AnimatedBlendShapes(ExpressionSet set)
        {
            var result = new HashSet<(string, string)>();
            foreach (var clip in set.expressions.Select(e => e.clip).Where(c => c != null).Distinct())
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(SkinnedMeshRenderer) || !binding.propertyName.StartsWith(BlendShapePrefix)) continue;
                    result.Add((binding.path, binding.propertyName.Substring(BlendShapePrefix.Length)));
                }
            }
            return result;
        }
    }
}
