using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class LibraryClipUtility
    {
        public static string Id(AnimationClip clip)
        {
            return clip != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId)
                ? guid + ":" + localId : null;
        }

        public static bool IsHidden(ExpressionSet set, AnimationClip clip)
        {
            var id = Id(clip);
            return id != null && set.hiddenLibraryClipIds != null && set.hiddenLibraryClipIds.Contains(id);
        }

        public static void SetHidden(ExpressionSet set, AnimationClip clip, bool hidden)
        {
            var id = Id(clip);
            if (id == null) return;
            Undo.RecordObject(set, "ライブラリの表示を変更");
            if (set.hiddenLibraryClipIds == null) set.hiddenLibraryClipIds = new System.Collections.Generic.List<string>();
            if (hidden && !set.hiddenLibraryClipIds.Contains(id)) set.hiddenLibraryClipIds.Add(id);
            if (!hidden) set.hiddenLibraryClipIds.Remove(id);
            EditorUtility.SetDirty(set);
        }

        public static string Rename(ExpressionSet set, AnimationClip clip, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "名前を入力してください";
            var stem = name.Split('.')[0].ToUpperInvariant();
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") ||
                new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem))
                return "ファイル名に使用できない名前です";
            var path = AssetDatabase.GetAssetPath(clip);
            if (clip == null || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) || AssetDatabase.IsSubAsset(clip))
                return "Assets内の独立したアニメーションだけ名前を変更できます";


            var fileName = name;
            var destination = Path.GetDirectoryName(path).Replace('\\', '/') + "/" + fileName + ".anim";
            if (!string.Equals(path, destination, StringComparison.OrdinalIgnoreCase) && File.Exists(destination))
                return "同名のファイルが既にあります";
            var error = AssetDatabase.RenameAsset(path, fileName);
            if (!string.IsNullOrEmpty(error)) return error;
            clip.name = name;
            foreach (var expression in set.expressions.Where(e => e.clip == clip)) expression.name = name;
            foreach (var part in set.parts.Where(p => p.clip == clip)) part.name = name;
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(set);
            return null;
        }
    }
}
