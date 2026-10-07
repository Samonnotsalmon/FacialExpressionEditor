using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// サムネイルのキャッシュのキー。表情エディタとビルド（メニューのアイコン）で同じキーを使い、
    /// 表情エディタで描いたサムネイルをアイコンにも使えるようにする。
    /// クリップや顔バリアントの内容と、アバターの今の顔（シェイプキーの値）が変わればキーも変わる。
    /// </summary>
    internal static class ThumbnailKeys
    {
        /// <summary>
        /// アバターの今の顔（全レンダラーのシェイプキーの値）。
        /// </summary>
        public static string FaceHash(GameObject avatar)
        {
            var hash = new Hash128();
            foreach (var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                hash.Append(renderer.name);
                for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++) hash.Append(renderer.GetBlendShapeWeight(i));
            }
            return hash.ToString();
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
                    foreach (var key in AnimationUtility.GetEditorCurve(clip, binding).keys)
                    {
                        hash.Append(key.time);
                        hash.Append(key.value);
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
            return $"expr|{expression.id}|{AssetKey(expression.clip)}|{AssetKey(overridden?.clip)}|{AssetKey(variant)}|{faceHash}";
        }

        public static string Part(FacialPart part, string faceHash)
        {
            return $"part|{part.id}|{AssetKey(part.clip)}|{string.Join(",", part.properties)}|{faceHash}";
        }

        public static string Neutral(string faceHash)
        {
            return $"neutral|{faceHash}";
        }
    }
}
