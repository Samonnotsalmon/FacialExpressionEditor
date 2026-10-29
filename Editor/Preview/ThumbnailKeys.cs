using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// サムネイルのキャッシュのキー。表情エディタとビルド（メニューのアイコン）で同じキーを使い、
    /// 表情エディタで描いたサムネイルをアイコンにも使えるようにする。
    /// クリップや顔バリアントの内容と、アバターの外見・表示状態が変わればキーも変わる。
    /// </summary>
    internal static class ThumbnailKeys
    {
        /// <summary>
        /// アバターの外見（表示状態、メッシュ、マテリアル、シェイプキーの値）。
        /// </summary>
        public static string FaceHash(GameObject avatar, ExpressionSet set = null)
        {
            var hash = new Hash128();
            hash.Append("appearance-preview-v4");
            AppendVector(ref hash, avatar.transform.lossyScale);
            foreach (var transform in avatar.GetComponentsInChildren<Transform>(true))
            {
                hash.Append(AnimationUtility.CalculateTransformPath(transform, avatar.transform));
                // Preview normalizes the avatar root's position/rotation. Keep the key
                // reusable for the normalized build clone and include only effective root scale.
                if (transform != avatar.transform)
                {
                    AppendVector(ref hash, transform.localPosition);
                    var rotation = transform.localRotation;
                    hash.Append(rotation.x); hash.Append(rotation.y); hash.Append(rotation.z); hash.Append(rotation.w);
                    AppendVector(ref hash, transform.localScale);
                }
                hash.Append(transform.gameObject.activeSelf ? 1 : 0);
            }
            var descriptor = avatar.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                AppendVector(ref hash, descriptor.ViewPosition);
                hash.Append((int)descriptor.lipSync);
                hash.Append(descriptor.VisemeSkinnedMesh != null
                    ? AnimationUtility.CalculateTransformPath(descriptor.VisemeSkinnedMesh.transform, avatar.transform) : "-");
                foreach (var viseme in descriptor.VisemeBlendShapes ?? new string[0]) hash.Append(viseme ?? "");
                hash.Append(descriptor.MouthOpenBlendShapeName ?? "");
            }
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                hash.Append(AnimationUtility.CalculateTransformPath(renderer.transform, avatar.transform));
                hash.Append(renderer.gameObject.activeInHierarchy ? 1 : 0);
                hash.Append(renderer.enabled ? 1 : 0);
                hash.Append(renderer.forceRenderingOff ? 1 : 0);
                var mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                hash.Append(AssetKey(mesh));
                foreach (var material in renderer.sharedMaterials) hash.Append(AssetKey(material));
            }
            hash.Append(set != null ? set.previewHeight : 0.03f);
            hash.Append(set != null ? set.previewZoom : 1f);
            foreach (var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                hash.Append(renderer.name);
                for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++) hash.Append(renderer.GetBlendShapeWeight(i));
            }
            return hash.ToString();
        }

        private static void AppendVector(ref Hash128 hash, Vector3 value)
        {
            hash.Append(value.x); hash.Append(value.y); hash.Append(value.z);
        }

        // 中身のハッシュ。オブジェクトが変更されていない間（変更回数が同じ間）は使い回す。
        private static readonly Dictionary<(int, int), string> ContentHashes = new Dictionary<(int, int), string>();

        /// <summary>
        /// アセットの中身から作るキー。保存前の変更（表情の編集ウィンドウでの編集など）でも変わり、
        /// 保存やプレイモードに入るときのリロードでは変わらない（プレイ直前に用意したメニューのアイコンをビルドで使えるように）。
        /// </summary>
        public static string AssetKey(Object asset)
        {
            if (asset == null) return "-";
            var key = (asset.GetInstanceID(), EditorUtility.GetDirtyCount(asset));
            if (!ContentHashes.TryGetValue(key, out var hash))
            {
                hash = ContentHash(asset);
                ContentHashes[key] = hash;
            }
            return hash;
        }

        private static string ContentHash(Object asset)
        {
            var hash = new Hash128();
            if (asset is AnimationClip clip)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    hash.Append(binding.path);
                    hash.Append(binding.propertyName);
                    hash.Append(binding.type.FullName);
                    foreach (var key in AnimationUtility.GetEditorCurve(clip, binding).keys)
                    {
                        hash.Append(key.time);
                        hash.Append(key.value);
                        hash.Append(key.inTangent); hash.Append(key.outTangent);
                        hash.Append(key.inWeight); hash.Append(key.outWeight); hash.Append((int)key.weightedMode);
                    }
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    hash.Append(binding.path);
                    hash.Append(binding.propertyName);
                    foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                    {
                        hash.Append(key.time);
                        hash.Append(key.value != null ? AssetDatabase.GetAssetPath(key.value) + "/" + key.value.name : "-");
                    }
                }
            }
            else
            {
                hash.Append(EditorJsonUtility.ToJson(asset));
            }
            return hash.ToString();
        }

        public static string Clip(AnimationClip clip, string faceHash)
        {
            return $"clip|{AssetKey(clip)}|{faceHash}";
        }

        public static string Expression(Expression expression, FaceVariant variant, string faceHash)
        {
            var overridden = variant != null ? variant.FindOverride(expression.id) : null;
            return $"expr|{expression.id}|{expression.baseFaceBaked}|{expression.freezeAnimation}|{expression.freezePosition}|{AssetKey(expression.clip)}|{AssetKey(overridden?.clip)}|{AssetKey(variant)}|{faceHash}";
        }

        public static string Part(FacialPart part, string faceHash)
        {
            return $"part|{part.id}|{AssetKey(part.clip)}|{string.Join(",", part.properties)}|{faceHash}";
        }

        public static string BaseFace(FaceVariant variant, string faceHash) => $"base|{AssetKey(variant)}|{faceHash}";

        public static string Neutral(string faceHash)
        {
            return $"neutral|{faceHash}";
        }
    }
}
