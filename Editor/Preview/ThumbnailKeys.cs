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

        public static string AssetKey(Object asset)
        {
            if (asset == null) return "-";
            var path = AssetDatabase.GetAssetPath(asset);
            return AssetDatabase.AssetPathToGUID(path) + ":" + AssetDatabase.GetAssetDependencyHash(path);
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
