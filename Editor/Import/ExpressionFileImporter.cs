using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    public static class ExpressionFileImporter
    {
        public static IEnumerable<AnimationClip> Clips(IEnumerable<Object> sources)
        {
            var clips = new HashSet<AnimationClip>();
            foreach (var source in sources.Where(s => s != null))
            {
                if (source is AnimationClip clip) { clips.Add(clip); continue; }
                var path = AssetDatabase.GetAssetPath(source);
                if (!AssetDatabase.IsValidFolder(path)) continue;
                foreach (var file in AssetDatabase.FindAssets("t:AnimationClip", new[] { path }).Select(AssetDatabase.GUIDToAssetPath).Distinct())
                    foreach (var found in AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
                        clips.Add(found);
            }
            return clips.OrderBy(c => c.name).ThenBy(AssetDatabase.GetAssetPath);
        }

        public static int Import(ExpressionSet set, IEnumerable<Object> sources)
        {
            Undo.RecordObject(set, "表情ファイルを取り込み");
            var before = set.expressions.Count;
            foreach (var clip in Clips(sources))
            {
                var expression = ExpressionSetUtility.FindOrCreateExpression(set, clip);
                if (!ExpressionSetUtility.OwnsClip(set, expression.clip)) ExpressionSetUtility.MakeClipEditable(set, expression);
            }
            EditorUtility.SetDirty(set); AssetDatabase.SaveAssets();
            return set.expressions.Count - before;
        }
    }
}
