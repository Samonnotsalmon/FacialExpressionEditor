using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class AssetPathUtility
    {
        public static string FolderOf(Object asset)
        {
            return Path.GetDirectoryName(AssetDatabase.GetAssetPath(asset))?.Replace('\\', '/');
        }

        /// <summary>
        /// parent の下に names のフォルダを順に作り、最後のフォルダのパスを返す（既にあればそのまま使う）。
        /// </summary>
        public static string EnsureFolder(string parent, params string[] names)
        {
            var current = parent;
            foreach (var name in names)
            {
                var next = $"{current}/{name}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, name);
                current = next;
            }
            return current;
        }

        public static string SafeFileName(string name, string fallback = "New")
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrEmpty(safe) ? fallback : safe;
        }
    }
}
