using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private enum LibraryFilter { All, Unassigned, Used }

        private static readonly string[] FilterLabels = { "すべて", "未割り当て", "使用中" };
        private const float LibraryCellWidth = 86f;
        private const float LibraryCellHeight = 102f;

        [SerializeField] private LibraryFilter _libraryFilter;
        [SerializeField] private int _libraryFolderIndex;
        [SerializeField] private string _librarySearch = "";

        private Vector2 _libraryScroll;
        private bool _libraryDirty = true;
        private List<AnimationClip> _libraryClips = new List<AnimationClip>();
        private List<string> _libraryFolders = new List<string>();
        private List<AnimationClip> _filteredClips = new List<AnimationClip>();

        private void MarkLibraryDirty() => _libraryDirty = true;

        /// <summary>
        /// 登録フォルダ（サブフォルダも含む）と、表情データの Expressions/ のクリップ、表情で使っているクリップを集める。
        /// </summary>
        private void RefreshLibraryIfNeeded(ExpressionSet set)
        {
            if (!_libraryDirty) return;
            _libraryDirty = false;

            _libraryFolders = set.libraryFolderGuids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(AssetDatabase.IsValidFolder)
                .ToList();

            var expressionsFolder = $"{AssetPathUtility.FolderOf(set)}/Expressions";
            var searchFolders = _libraryFolders.ToList();
            if (AssetDatabase.IsValidFolder(expressionsFolder)) searchFolders.Add(expressionsFolder);

            var clips = new List<AnimationClip>();
            if (searchFolders.Count > 0)
            {
                clips.AddRange(AssetDatabase.FindAssets("t:AnimationClip", searchFolders.ToArray())
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Distinct()
                    .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>)
                    .Where(c => c != null));
            }
            clips.AddRange(set.expressions.Select(e => e.clip).Where(c => c != null));

            _libraryClips = clips
                .Distinct()
                .OrderBy(AssetDatabase.GetAssetPath)
                .ToList();
        }

        private void DrawLibrary(ExpressionSet set)
        {
            EditorGUILayout.LabelField("クリップライブラリ", EditorStyles.boldLabel);

            _libraryFilter = (LibraryFilter)GUILayout.Toolbar((int)_libraryFilter, FilterLabels, EditorStyles.miniButton);
            _librarySearch = EditorGUILayout.TextField(_librarySearch, EditorStyles.toolbarSearchField);

            var folderLabels = new[] { "すべてのフォルダ" }.Concat(_libraryFolders.Select(ShortFolderName)).ToArray();
            using (new EditorGUILayout.HorizontalScope())
            {
                _libraryFolderIndex = Mathf.Clamp(EditorGUILayout.Popup(_libraryFolderIndex, folderLabels), 0, folderLabels.Length - 1);
                if (GUILayout.Button("フォルダを追加", EditorStyles.miniButton, GUILayout.Width(90)))
                {
                    AddLibraryFolder(set);
                }
            }

            var clips = _filteredClips;
            var unassigned = _libraryClips.Count(c => !_usage.IsUsed(c));
            EditorGUILayout.LabelField($"{clips.Count} 件（全 {_libraryClips.Count} 件中、未割り当て {unassigned} 件）", EditorStyles.miniLabel);

            _libraryScroll = EditorGUILayout.BeginScrollView(_libraryScroll);
            var rects = GridRects(clips.Count, LibraryCellWidth, LibraryCellHeight, LibraryWidth - 22);
            for (var i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                var rect = rects[i];
                if (!IsVisible(rect, _libraryScroll)) continue;

                var used = _usage.IsUsed(clip);
                HandleDragSource(rect, clip);
                if (DrawCell(rect, ClipThumbnail(clip), clip.name, IsSelected(SelectionKind.Clip, null, clip),
                        used ? null : "未割り当て"))
                {
                    var expression = set.expressions.Find(e => e.clip == clip);
                    if (expression != null) Select(SelectionKind.Expression, expression.id, null);
                    else Select(SelectionKind.Clip, null, clip);
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField("クリップを中央の表やフォルダへドラッグして割り当てます。", EditorStyles.wordWrappedMiniLabel);
        }

        private List<AnimationClip> FilteredClips()
        {
            IEnumerable<AnimationClip> clips = _libraryClips;

            if (_libraryFolderIndex > 0 && _libraryFolderIndex - 1 < _libraryFolders.Count)
            {
                var folder = _libraryFolders[_libraryFolderIndex - 1] + "/";
                clips = clips.Where(c => AssetDatabase.GetAssetPath(c).StartsWith(folder));
            }

            switch (_libraryFilter)
            {
                case LibraryFilter.Unassigned: clips = clips.Where(c => !_usage.IsUsed(c)); break;
                case LibraryFilter.Used: clips = clips.Where(c => _usage.IsUsed(c)); break;
            }

            if (!string.IsNullOrWhiteSpace(_librarySearch))
            {
                var search = _librarySearch.Trim();
                clips = clips.Where(c => c.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return clips.ToList();
        }

        private void AddLibraryFolder(ExpressionSet set)
        {
            var absolute = EditorUtility.OpenFolderPanel("ライブラリに追加するフォルダ", "Assets", "");
            if (string.IsNullOrEmpty(absolute)) return;

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            absolute = Path.GetFullPath(absolute).Replace('\\', '/');
            if (!absolute.StartsWith(projectRoot + "/"))
            {
                EditorUtility.DisplayDialog("フォルダを追加", "プロジェクトの中のフォルダを選んでください。", "OK");
                return;
            }

            var relative = absolute.Substring(projectRoot.Length + 1);
            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(relative);
            if (folder == null || !AssetDatabase.IsValidFolder(relative))
            {
                EditorUtility.DisplayDialog("フォルダを追加", "このフォルダは追加できません。", "OK");
                return;
            }

            Modify(set, "ライブラリにフォルダを追加", () => FxImporter.AddFolder(set, folder));
            MarkLibraryDirty();
        }

        private static string ShortFolderName(string path)
        {
            var parts = path.Split('/');
            return parts.Length <= 2 ? path : $"{parts[parts.Length - 2]}/{parts[parts.Length - 1]}";
        }

        private bool IsVisible(Rect rect, Vector2 scroll)
        {
            // スクロール範囲外のセルはサムネイルを要求しない（描画の予約を増やさないため）。
            return rect.yMax >= scroll.y - 200 && rect.y <= scroll.y + position.height + 200;
        }
    }
}
