using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Samon.FacialExpressionEditor.Editor
{
    // 明示実行専用。インポート時やウィンドウを開いた際には一括移行しない。
    internal static class GeneratedAssetMigration
    {
        [Serializable]
        internal sealed class Entry
        {
            public string from;
            public string to;
            public string guid;
        }

        public static List<Entry> Plan(string root = AvatarSetup.DataFolder, string oldIcons = "Assets/NotSalmon/LipSyncIcons")
        {
            return AssetDatabase.GetAllAssetPaths()
                .Where(p => !AssetDatabase.IsValidFolder(p) &&
                    (p.StartsWith(root + "/", StringComparison.Ordinal) || p.StartsWith(oldIcons + "/", StringComparison.Ordinal)))
                .Where(p => new[] { ".anim", ".asset", ".prefab", ".png" }.Contains(Path.GetExtension(p).ToLowerInvariant()))
                .Select(p => new Entry {
                    from = p,
                    to = AssetPathUtility.PrefixedPath(p.StartsWith(oldIcons + "/", StringComparison.Ordinal)
                        ? root + "/Icons/LipSync/" + p.Substring(oldIcons.Length + 1) : p),
                    guid = AssetDatabase.AssetPathToGUID(p)
                }).Where(e => e.from != e.to).OrderBy(e => e.from, StringComparer.Ordinal).ToList();
        }

        public static void Apply(List<Entry> plan)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Playモードを終了してください。");
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in plan)
            {
                if (AssetDatabase.AssetPathToGUID(entry.from) != entry.guid || string.IsNullOrEmpty(entry.guid))
                    throw new InvalidOperationException("移行対象が変わりました: " + entry.from);
                if (!destinations.Add(entry.to) || File.Exists(entry.to) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(entry.to, AssetPathToGUIDOptions.OnlyExistingAssets)))
                    throw new InvalidOperationException("移行先が重複しています: " + entry.to);
            }
            var moved = new List<Entry>();
            try
            {
                foreach (var entry in plan)
                {
                    var folder = Path.GetDirectoryName(entry.to).Replace('\\', '/');
                    AssetPathUtility.EnsureFolder("Assets", folder.Substring(7).Split('/'));
                    var error = AssetDatabase.MoveAsset(entry.from, entry.to);
                    if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                    moved.Add(entry);
                    if (AssetDatabase.AssetPathToGUID(entry.to) != entry.guid)
                        throw new IOException("GUIDを保持できませんでした: " + entry.to);
                }
            }
            catch
            {
                foreach (var entry in moved.AsEnumerable().Reverse())
                {
                    var error = AssetDatabase.MoveAsset(entry.to, entry.from);
                    if (!string.IsNullOrEmpty(error)) UnityEngine.Debug.LogError("移行の復元に失敗: " + error);
                }
                throw;
            }
        }

        public static int SynchronizeNames(string root = AvatarSetup.DataFolder)
        {
            var count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:ExpressionSet", new[] { root }))
            {
                var set = AssetDatabase.LoadAssetAtPath<ExpressionSet>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var expression in set.expressions)
                {
                    ExpressionSetUtility.MatchExpressionNameToFile(set, expression);
                    if (ExpressionSetUtility.OwnsClip(set, expression.clip)) AssetDatabase.SaveAssetIfDirty(expression.clip);
                }
                foreach (var part in set.parts)
                {
                    ExpressionSetUtility.MatchPartNameToFile(set, part);
                    if (ExpressionSetUtility.OwnsPartClip(set, part.clip)) AssetDatabase.SaveAssetIfDirty(part.clip);
                }
                AssetDatabase.SaveAssetIfDirty(set);
                count++;
            }
            return count;
        }
    }
}
