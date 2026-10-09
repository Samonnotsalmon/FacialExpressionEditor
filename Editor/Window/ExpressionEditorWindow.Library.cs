using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
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
        // 絞り込んでいるフォルダ（0 ならすべて、1 から順に登録フォルダ）。
        [SerializeField] private int _libraryFolderIndex;
        [SerializeField] private string _librarySearch = "";
        [SerializeField] private bool _showLibraryFolders = true;

        private Vector2 _libraryScroll;
        private bool _libraryDirty = true;
        private List<AnimationClip> _libraryClips = new List<AnimationClip>();
        private List<string> _libraryFolders = new List<string>();
        private Dictionary<string, int> _libraryFolderCounts = new Dictionary<string, int>();
        private readonly Dictionary<AnimationClip, string> _librarySourcePaths = new Dictionary<AnimationClip, string>();
        private List<AnimationClip> _filteredClips = new List<AnimationClip>();

        // 登録フォルダの一覧（Unity の標準のリスト。選ぶと絞り込み、下の＋で追加、－で選んでいるものを外す）。
        private ReorderableList _folderList;
        private ExpressionSet _folderListOf;

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
                .Where(c => { var e = ExpressionSetUtility.FindExpressionByClip(set, c); return e == null || e.clip == c; })
                .OrderBy(AssetDatabase.GetAssetPath)
                .ToList();
            _librarySourcePaths.Clear();
            foreach (var clip in _libraryClips)
            {
                var expression = ExpressionSetUtility.FindExpressionByClip(set, clip);
                _librarySourcePaths[clip] = AssetDatabase.GetAssetPath(expression?.originalClip != null ? expression.originalClip : clip);
            }
            _libraryFolderCounts = _libraryFolders.ToDictionary(f => f, f => _libraryClips.Count(c => _librarySourcePaths[c].StartsWith(f + "/")));
            _folderList = null;
        }

        private void DrawLibrary(ExpressionSet set)
        {
            EditorGUILayout.LabelField("クリップライブラリ", EditorStyles.boldLabel);
            var source = EditorGUILayout.ObjectField("取り込み", null, typeof(Object), false);
            if (source != null) ImportExpressionSources(set, new[] { source });
            var drop = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "クリップ／フォルダをD&Dで取り込み");
            var dropped = AcceptExpressionSources(drop).ToList();
            if (dropped.Count > 0) ImportExpressionSources(set, dropped);

            _libraryFilter = (LibraryFilter)GUILayout.Toolbar((int)_libraryFilter, FilterLabels, EditorStyles.miniButton);
            _librarySearch = EditorGUILayout.TextField(_librarySearch, EditorStyles.toolbarSearchField);

            DrawLibraryFolders(set);

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
                var expression = ExpressionSetUtility.FindExpressionByClip(set, clip);
                var moving = expression != null ? !expression.freezeAnimation && PreviewClips.IsTimeVarying(expression.clip) : PreviewClips.IsTimeVarying(clip);
                HandleDragSource(rect, clip);
                if (DrawCell(rect, expression != null ? ExpressionThumbnail(expression) : ClipThumbnail(clip), expression?.name ?? clip.name,
                        expression != null ? IsSelected(SelectionKind.Expression, expression.id, null) : IsSelected(SelectionKind.Clip, null, clip),
                        moving ? "▶ 動く表情" : used ? null : "未割り当て"))
                {
                    if (expression != null) Select(SelectionKind.Expression, expression.id, null);
                    else Select(SelectionKind.Clip, null, clip);
                }
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("＋ 新しい表情", EditorStyles.miniButton))
            {
                ShowNewExpressionMenu(set, _selectionKind == SelectionKind.Expression ? set.FindExpression(_selectedId) : null);
            }
            EditorGUILayout.LabelField("クリップを中央の表やフォルダへドラッグして割り当てます。Project のフォルダをここにドロップすると、ライブラリに入ります。",
                EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// 新しい表情を作る（今の顔から・空から・表情をコピー）。作ったら選んで、表情の編集ウィンドウで開く。
        /// assign を渡すと、作った表情をそこ（ジェスチャーのマスなど）に割り当てる。
        /// </summary>
        private void ShowNewExpressionMenu(ExpressionSet set, Expression copySource, System.Action<string> assign = null, string place = null)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("今の顔から"), false,
                () => CreateExpression(set, ExpressionSetUtility.NewExpressionSource.CurrentFace, null, assign, place));
            menu.AddItem(new GUIContent("空から（何も動かさない）"), false,
                () => CreateExpression(set, ExpressionSetUtility.NewExpressionSource.Empty, null, assign, place));
            if (copySource != null)
            {
                menu.AddItem(new GUIContent($"「{copySource.name}」をコピー"), false,
                    () => CreateExpression(set, ExpressionSetUtility.NewExpressionSource.CopyExpression, copySource, assign, place));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(assign == null ? "選んでいる表情をコピー" : "表情をコピー"));
            }
            menu.ShowAsContext();
        }

        private void CreateExpression(ExpressionSet set, ExpressionSetUtility.NewExpressionSource source, Expression copyFrom,
            System.Action<string> assign, string place)
        {
            var name = copyFrom != null ? $"{copyFrom.name} のコピー" : "新しい表情";
            for (var i = 2; set.expressions.Any(e => e.name == name); i++) name = copyFrom != null ? $"{copyFrom.name} のコピー {i}" : $"新しい表情 {i}";

            var expression = ExpressionSetUtility.CreateExpression(set, name, source, copyFrom, AvatarRoot, Variant);
            if (expression == null) return;
            if (assign != null) Modify(set, $"{place}に新しい表情を割り当て", () => assign(expression.id));
            MarkLibraryDirty();
            Select(SelectionKind.Expression, expression.id, null);
            ExpressionClipEditorWindow.Open(_avatar, expression);
        }

        private List<AnimationClip> FilteredClips()
        {
            IEnumerable<AnimationClip> clips = _libraryClips;

            if (_libraryFolderIndex > 0 && _libraryFolderIndex - 1 < _libraryFolders.Count)
            {
                var folder = _libraryFolders[_libraryFolderIndex - 1] + "/";
                clips = clips.Where(c => _librarySourcePaths.TryGetValue(c, out var sourcePath) && sourcePath.StartsWith(folder));
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
            ExpressionFileImporter.Import(set, new Object[] { folder });
            MarkLibraryDirty();
        }

        /// <summary>
        /// 登録フォルダの一覧。Unity の標準のリスト（Tags and Layers や Package Manager の Scoped Registries と同じ）で、
        /// フォルダを選ぶとライブラリをそのフォルダに絞り込み、下の＋で追加、－で選んでいるフォルダを外す（確かめてから）。
        /// </summary>
        private void DrawLibraryFolders(ExpressionSet set)
        {
            _libraryFolderIndex = Mathf.Clamp(_libraryFolderIndex, 0, _libraryFolders.Count);
            var filter = _libraryFolderIndex > 0 ? ShortFolderName(_libraryFolders[_libraryFolderIndex - 1]) : "すべて";
            _showLibraryFolders = EditorGUILayout.Foldout(_showLibraryFolders, $"フォルダ（{_libraryFolders.Count}）　表示：{filter}", true);
            if (!_showLibraryFolders) return;

            if (_folderList == null || _folderListOf != set || _folderList.list != _libraryFolders)
            {
                _folderListOf = set;
                _folderList = new ReorderableList(_libraryFolders, typeof(string), false, true, true, true)
                {
                    elementHeight = EditorGUIUtility.singleLineHeight + 2,
                    drawHeaderCallback = rect => DrawFolderListHeader(rect),
                    drawElementCallback = (rect, index, active, focused) =>
                    {
                        var folder = _libraryFolders[index];
                        var count = _libraryFolderCounts.TryGetValue(folder, out var c) ? c : 0;
                        GUI.Label(new Rect(rect.x, rect.y + 1, rect.width - 44, rect.height), new GUIContent(ShortFolderName(folder), folder));
                        GUI.Label(new Rect(rect.xMax - 44, rect.y + 1, 44, rect.height), $"{count} 件", RightMiniLabel);
                    },
                    onSelectCallback = list => _libraryFolderIndex = list.index + 1,
                    onAddCallback = list =>
                    {
                        AddLibraryFolder(set);
                        GUIUtility.ExitGUI();
                    },
                    onCanRemoveCallback = list => list.index >= 0 && list.index < _libraryFolders.Count,
                    onRemoveCallback = list => RemoveLibraryFolder(set, _libraryFolders[list.index]),
                };
            }
            _folderList.index = _libraryFolderIndex - 1;
            _folderList.DoLayoutList();
        }

        private void DrawFolderListHeader(Rect rect)
        {
            GUI.Label(rect, "クリックで絞り込み", EditorStyles.miniLabel);
            if (_libraryFolderIndex > 0 && GUI.Button(new Rect(rect.xMax - 64, rect.y, 64, rect.height), "すべて表示", EditorStyles.miniButton))
            {
                _libraryFolderIndex = 0;
            }
        }

        private static GUIStyle _rightMiniLabel;
        private static GUIStyle RightMiniLabel => _rightMiniLabel ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };

        private void RemoveLibraryFolder(ExpressionSet set, string folder)
        {
            if (!EditorUtility.DisplayDialog("フォルダを外す",
                    $"「{folder}」をライブラリから外しますか？\nフォルダとアニメーションは消えません。表情に使っているクリップは、ライブラリに残ります。",
                    "外す", "キャンセル"))
            {
                return;
            }

            var guid = AssetDatabase.AssetPathToGUID(folder);
            Modify(set, "ライブラリからフォルダを外す", () => set.libraryFolderGuids.Remove(guid));
            _libraryFolderIndex = 0;
            MarkLibraryDirty();
            GUIUtility.ExitGUI();
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

        private void ImportExpressionSources(ExpressionSet set, IEnumerable<Object> sources)
        {
            var assets = sources.ToList();
            Undo.RecordObject(set, "取り込み元を追加");
            foreach (var folder in assets.OfType<DefaultAsset>().Where(f => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(f)))) FxImporter.AddFolder(set, folder);
            var count = ExpressionFileImporter.Import(set, assets);
            MarkLibraryDirty(); InvalidateDetailPreview();
            ShowNotification(new GUIContent($"{count} 件をこのベース顔専用に取り込みました"));
        }

        private static IEnumerable<Object> AcceptExpressionSources(Rect rect)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition) || (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)) return new Object[0];
            var assets = DragAndDrop.objectReferences.Where(o => o is AnimationClip || o is DefaultAsset && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(o))).ToArray();
            if (assets.Length == 0) return assets;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            var perform = evt.type == EventType.DragPerform;
            if (perform) DragAndDrop.AcceptDrag();
            evt.Use();
            return perform ? assets : new Object[0];
        }
    }
}
