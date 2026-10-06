using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情データの複製と、新しい表情の作成を行う。
    /// </summary>
    public static class ExpressionSetUtility
    {
        public enum NewExpressionSource
        {
            // アバターの今の顔（ベース顔）をそのままクリップにする。
            CurrentFace,
            // 既存の表情のクリップを複製する。
            CopyExpression,
            // 何も動かさないクリップから始める。
            Empty,
        }

        /// <summary>
        /// 表情データを複製する。表情・表情セット・パーツの設定を引き継ぎ、表情のIDも同じものを使うので、
        /// 顔バリアントの差し替えは複製した表情データでもそのまま使える。
        /// </summary>
        public static ExpressionSet Duplicate(ExpressionSet source, string path)
        {
            var copy = Object.Instantiate(source);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            return copy;
        }

        /// <summary>
        /// 新しい表情クリップを 表情データのフォルダ/Expressions/ に作り、表情データに追加する。
        /// </summary>
        public static Expression CreateExpression(ExpressionSet set, string name, NewExpressionSource source,
            Expression copyFrom, GameObject avatarRoot, FaceVariant variant)
        {
            var folder = AssetPathUtility.EnsureFolder(AssetPathUtility.FolderOf(set), "Expressions");
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{AssetPathUtility.SafeFileName(name)}.anim");

            AnimationClip clip;
            if (source == NewExpressionSource.CopyExpression)
            {
                if (copyFrom == null) return null;
                var sourceClip = variant?.FindOverride(copyFrom.id)?.clip ?? copyFrom.clip;
                if (sourceClip == null || !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(sourceClip), path)) return null;
                clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            }
            else
            {
                clip = new AnimationClip();
                if (source == NewExpressionSource.CurrentFace) WriteCurrentFace(clip, set, avatarRoot, variant);
                AssetDatabase.CreateAsset(clip, path);
            }

            Undo.RecordObject(set, "新しい表情を作成");
            var expression = new Expression { name = name, clip = clip };
            set.expressions.Add(expression);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return expression;
        }

        /// <summary>
        /// 表情で動くシェイプキー（既存の表情が動かすものと、顔バリアントのベース顔）に、アバターの今の値を書き込む。
        /// </summary>
        private static void WriteCurrentFace(AnimationClip clip, ExpressionSet set, GameObject avatarRoot, FaceVariant variant)
        {
            var keys = FaceVariantUtility.AnimatedBlendShapes(set);
            if (variant != null)
            {
                foreach (var key in variant.baseFace.Where(k => k.enabled)) keys.Add((key.path, key.blendShape));
            }

            foreach (var (path, blendShape) in keys.OrderBy(k => k.path).ThenBy(k => k.blendShape))
            {
                var transform = string.IsNullOrEmpty(path) ? avatarRoot.transform : avatarRoot.transform.Find(path);
                var renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
                var index = renderer != null && renderer.sharedMesh != null ? renderer.sharedMesh.GetBlendShapeIndex(blendShape) : -1;
                if (index < 0) continue;

                var binding = EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + blendShape);
                AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(new Keyframe(0, renderer.GetBlendShapeWeight(index))));
            }
        }
    }
}
