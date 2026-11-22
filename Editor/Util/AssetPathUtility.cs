using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class AssetPathUtility
    {
        public const string Prefix = "NSL_";
        public const string LipSyncIconsFolder = "Assets/NSL_FacialExpressionEditor/Icons/LipSync";

        public static string AssetName(string name) => Prefix + DisplayName(SafeFileName(name));

        public static string DisplayName(string name) => name != null && name.StartsWith(Prefix, System.StringComparison.Ordinal)
            ? name.Substring(Prefix.Length) : name;

        public static string PrefixedPath(string path)
        {
            var extension = Path.GetExtension(path);
            if (!new[] { ".asset", ".prefab", ".png" }.Contains(extension.ToLowerInvariant())) return path;
            return Path.GetDirectoryName(path).Replace('\\', '/') + "/" + AssetName(Path.GetFileNameWithoutExtension(path)) + extension;
        }

        public static string UniqueAssetPath(string path) => AssetDatabase.GenerateUniqueAssetPath(PrefixedPath(path));

        public static string SetupFolder(ExpressionSet set)
        {
            var folder = FolderOf(set);
            return set.usesSettingsFolder ? Path.GetDirectoryName(folder)?.Replace('\\', '/') : folder;
        }

        public static string SettingsFolder(ExpressionSet set) => FolderOf(set);

        public static string NewSettingsPath(string path)
        {
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            return UniqueAssetPath(EnsureFolder(parent, "Settings") + "/" + Path.GetFileName(path));
        }

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
