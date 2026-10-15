using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    public static class ExpressionFileImporter
    {
        public static IEnumerable<string> Folders(ExpressionSet set)
        {
            var result = new SortedSet<string>();
            void Add(string path)
            {
                if (!AssetDatabase.IsValidFolder(path) || !result.Add(path)) return;
                foreach (var child in AssetDatabase.GetSubFolders(path)) Add(child);
            }
            foreach (var guid in set.libraryFolderGuids) Add(AssetDatabase.GUIDToAssetPath(guid));
            return result;
        }

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

        public static int Import(ExpressionSet set, IEnumerable<Object> sources, GameObject avatarRoot = null)
        {
            Undo.RecordObject(set, "表情ファイルを取り込み");
            var before = set.expressions.Count;
            var assets = sources.ToList();
            foreach (var folder in assets.OfType<DefaultAsset>().Where(f => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(f)))) FxImporter.AddFolder(set, folder);
            foreach (var clip in Clips(assets))
            {
                var expression = ExpressionSetUtility.FindOrCreateExpression(set, clip);
                if (!ExpressionSetUtility.OwnsClip(set, expression.clip)) ExpressionSetUtility.MakeClipEditable(set, expression);
                LipSyncProtection.Remove(expression.clip, avatarRoot, set);
            }
            EditorUtility.SetDirty(set); AssetDatabase.SaveAssets();
            return set.expressions.Count - before;
        }
    }
}
