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
            variant.useCapturedValues = set.independentClips;
            DetectBaseFace(variant, set, avatarRoot);
            AssetDatabase.SaveAssets();
            return variant;
        }

        /// <summary>
        /// 元Prefabと値が違うシェイプキーを、ベース顔の候補として検出する。
        /// 表情データのクリップが動かすシェイプキーと、顔のメッシュのシェイプキーは有効、それ以外は無効の状態で追加する。
        /// 既に登録済みのシェイプキーは、有効・無効の設定を引き継ぐ。戻り値は、元Prefabが見つからなかったレンダラーのパス。
        /// </summary>
        public static List<string> DetectBaseFace(FaceVariant variant, ExpressionSet set, GameObject avatarRoot)
        {
            var detected = CompareWithOriginal(set, avatarRoot, variant.baseFace, out var missingReference);
            Undo.RecordObject(variant, "ベース顔を検出");
            variant.baseFace = detected;
            EditorUtility.SetDirty(variant);
            return missingReference;
        }

        /// <summary>
        /// アバターの顔と元Prefabの違い（ベース顔の候補）。顔バリアントは作らない。
        /// 調べるのは、表情データのクリップが動かすメッシュと顔のメッシュ。previous にあるシェイプキーは有効・無効を引き継ぐ。
        /// </summary>
        public static List<BaseFaceKey> CompareWithOriginal(ExpressionSet set, GameObject avatarRoot, IEnumerable<BaseFaceKey> previous,
            out List<string> missingReference)
        {
            missingReference = new List<string>();
            var animated = set != null ? AnimatedBlendShapes(set) : new HashSet<(string path, string blendShape)>();
            var previousKeys = (previous ?? Enumerable.Empty<BaseFaceKey>())
                .GroupBy(k => (k.path, k.blendShape))
                .ToDictionary(g => g.Key, g => g.First());
            var detected = new List<BaseFaceKey>();

            var face = AvatarSetup.FaceRenderer(avatarRoot);
            var facePath = face != null ? AnimationUtility.CalculateTransformPath(face.transform, avatarRoot.transform) : null;
            var paths = animated.Select(a => a.path).ToList();
            if (facePath != null) paths.Add(facePath);
            if (set != null && set.faceMeshPaths.Count > 0) paths = set.faceMeshPaths.ToList();

            foreach (var path in paths.Distinct())
            {
                var transform = string.IsNullOrEmpty(path) ? avatarRoot.transform : avatarRoot.transform.Find(path);
                var renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
                if (renderer == null || renderer.sharedMesh == null) continue;

                var reference = OriginalRenderer(renderer);
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
                        enabled = previousKeys.TryGetValue((path, name), out var old)
                            ? old.enabled
                            : animated.Contains((path, name)) || path == facePath || (set != null && set.faceMeshPaths.Contains(path)),
                    });
                }
            }
            return detected;
        }

        /// <summary>
        /// 比べる元の顔のレンダラー。プレハブの元をたどり、モデル（FBX）のすぐ上のプレハブ（作者のプレハブ）を使う。
        /// プレハブが無くモデルだけなら、モデルを使う。プレハブでなければ null。
        /// </summary>
        public static SkinnedMeshRenderer OriginalRenderer(SkinnedMeshRenderer renderer)
        {
            SkinnedMeshRenderer found = null;
            SkinnedMeshRenderer model = null;
            var current = renderer;
            for (var depth = 0; depth < 16; depth++)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (source == null) break;
                if (PrefabUtility.GetPrefabAssetType(source.gameObject) == PrefabAssetType.Model) model = source;
                else found = source;
                current = source;
            }
            return found ?? model;
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
        /// 差し替えクリップに、ベース顔の差分（バリアントの値 − 元Prefabの値）を書き込む。
        /// 反映済みのものには何もしない。戻り値は反映したかどうか。
        /// </summary>
        public static bool BakeBaseFace(FaceVariant variant, ExpressionOverride entry)
        {
            if (entry.baseFaceBaked || entry.clip == null) return false;

            Undo.RecordObject(entry.clip, "ベース顔を反映");
            foreach (var key in variant.baseFace)
            {
                if (!key.enabled) continue;

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
        /// ビルドとプレビューで使う差し替え。中身が共有の表情と同じ（複製したまま編集していない）差し替えクリップは、
        /// 差し替えていないものとして扱い、ベース顔の設定がそのまま効くようにする。
        /// </summary>
        public static ExpressionOverride EffectiveOverride(FaceVariant variant, Expression expression)
        {
            var entry = variant != null ? variant.FindOverride(expression.id) : null;
            return entry != null && IsEdited(entry, expression) ? entry : null;
        }

        // 中身の比較はカーブを全部読むので、クリップが変わらない間は結果を使い回す。
        private static readonly Dictionary<(int, int, Hash128, int, int, Hash128), bool> EditedCache =
            new Dictionary<(int, int, Hash128, int, int, Hash128), bool>();

        /// <summary>
        /// 差し替えクリップの中身が、共有の表情クリップと違うかどうか。
        /// </summary>
        public static bool IsEdited(ExpressionOverride entry, Expression expression)
        {
            if (entry?.clip == null) return false;
            if (expression.clip == null) return true;

            var key = (entry.clip.GetInstanceID(), EditorUtility.GetDirtyCount(entry.clip), DependencyHash(entry.clip),
                expression.clip.GetInstanceID(), EditorUtility.GetDirtyCount(expression.clip), DependencyHash(expression.clip));
            if (!EditedCache.TryGetValue(key, out var edited))
            {
                edited = !SameCurves(entry.clip, expression.clip);
                EditedCache[key] = edited;
            }
            return edited;
        }

        private static Hash128 DependencyHash(AnimationClip clip)
        {
            return AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(clip));
        }

        private static bool SameCurves(AnimationClip a, AnimationClip b)
        {
            var floatA = AnimationUtility.GetCurveBindings(a);
            var floatB = new HashSet<EditorCurveBinding>(AnimationUtility.GetCurveBindings(b));
            if (floatA.Length != floatB.Count) return false;
            foreach (var binding in floatA)
            {
                if (!floatB.Contains(binding)) return false;
                var keysA = AnimationUtility.GetEditorCurve(a, binding).keys;
                var keysB = AnimationUtility.GetEditorCurve(b, binding).keys;
                if (keysA.Length != keysB.Length) return false;
                for (var i = 0; i < keysA.Length; i++)
                {
                    if (!Mathf.Approximately(keysA[i].time, keysB[i].time) || !Mathf.Approximately(keysA[i].value, keysB[i].value)) return false;
                }
            }

            var objectA = AnimationUtility.GetObjectReferenceCurveBindings(a);
            var objectB = new HashSet<EditorCurveBinding>(AnimationUtility.GetObjectReferenceCurveBindings(b));
            if (objectA.Length != objectB.Count) return false;
            foreach (var binding in objectA)
            {
                if (!objectB.Contains(binding)) return false;
                var keysA = AnimationUtility.GetObjectReferenceCurve(a, binding);
                var keysB = AnimationUtility.GetObjectReferenceCurve(b, binding);
                if (keysA.Length != keysB.Length) return false;
                for (var i = 0; i < keysA.Length; i++)
                {
                    if (!Mathf.Approximately(keysA[i].time, keysB[i].time) || keysA[i].value != keysB[i].value) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// この顔のこの表情だけの値を設定する。value が null なら外す（ベース顔を適用した値に戻る）。
        /// </summary>
        public static void SetFaceValue(FaceVariant variant, string expressionId, string path, string blendShape, float? value)
        {
            Undo.RecordObject(variant, value != null ? "この顔だけの値を変更" : "この顔だけの値を外す");
            var faceValues = variant.FindFaceValues(expressionId);
            if (faceValues == null)
            {
                if (value == null) return;
                faceValues = new ExpressionFaceValues { expressionId = expressionId };
                variant.faceValues.Add(faceValues);
            }

            var entry = faceValues.Find(path, blendShape);
            if (value == null)
            {
                if (entry != null) faceValues.values.Remove(entry);
                if (faceValues.values.Count == 0) variant.faceValues.Remove(faceValues);
            }
            else if (entry == null)
            {
                faceValues.values.Add(new BlendShapeValue { path = path, blendShape = blendShape, value = value.Value });
            }
            else
            {
                entry.value = value.Value;
            }
            EditorUtility.SetDirty(variant);
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
