using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情の割り当て画面。左にクリップライブラリ、中央に表情メニュー（とジェスチャー）・パーツ、右にプレビューと設定。
    /// ライブラリやプロジェクトウィンドウのクリップを、ドラッグ＆ドロップで割り当てる。
    /// アバターのプレハブをドロップして始めると、表情データと表情設定のプレハブを作る（プレハブは各アバターの中へ入れて使う）。
    /// </summary>
    internal partial class ExpressionEditorWindow : EditorWindow
    {
        private const float LibraryWidth = 300f;
        private const float DetailWidth = 280f;

        private enum Tab { Menu, Parts, Face, AfkContacts, Setup }

        // Fist：表情セットの表で、握り具合を使う手のFistのマスを選んだとき（握り込みの設定とプレビュー）。
        // OriginalClip：元FXの、コンタクト・PhysBoneなどで顔を動かすレイヤーのクリップ（_selectedId はレイヤー名）。
        private enum SelectionKind { None, Expression, Clip, Part, Fist, OriginalClip }

        private static readonly string[] TabLabels = { "メニュー・ジェスチャー", "パーツ", "まばたき・口", "既存ギミック", "アバター設定" };

        // 編集している表情設定（プロジェクトのプレハブか、シーンのアバターの中にあるもの）。
        [SerializeField] private FacialExpressionAvatar _avatar;
        [SerializeField] private Tab _tab;
        [SerializeField] private SelectionKind _selectionKind;
        [SerializeField] private string _selectedId;
        [SerializeField] private AnimationClip _selectedClip;
        [SerializeField] private float _triggerWeight = 1f;
        [SerializeField] private Hand _selectedHand;

        private FacePreview _preview;
        private string _faceHash = "";
        private ThumbnailCache _thumbnails;
        private ClipUsage _usage;
        private Vector2 _centerScroll;

        private ExpressionSet Set => _avatar != null ? _avatar.expressionSet : null;
        private FaceVariant Variant => _avatar != null ? _avatar.faceVariant : null;
        // プレビューと元FXの読み取りに使うアバター。
        private GameObject AvatarRoot => AvatarSetup.AvatarRootOf(_avatar);
        private VRCAvatarDescriptor Descriptor => AvatarRoot != null ? AvatarRoot.GetComponent<VRCAvatarDescriptor>() : null;

        // 表情設定の一覧。プロジェクトかシーンが変わったときだけ探し直す。
        private List<FacialExpressionAvatar> _setups;
        private List<FacialExpressionAvatar> Setups => _setups ??= AvatarSetup.FindSetups();

        // 元アバターのまばたきと口モーフキャンセラー。元FXを調べるので、変わったときだけ作り直す。
        private AvatarFaceDefaults _faceDefaults;
        private AvatarFaceDefaults FaceDefaults => _faceDefaults ??= AvatarFaceDefaults.Find(Descriptor, Set);

        [MenuItem("Tools/Samon/表情エディタ")]
        private static void OpenFromMenu()
        {
            // メニューから開いたときは、アバターを選ぶところから始める。
            Open(null);
            GetWindow<ExpressionEditorWindow>().BeginSelectAvatar();
        }

        /// <summary>
        /// 「新しく始める」画面で開く。avatar を渡すと、そのアバターをドロップした状態から始める。
        /// </summary>
        public static void OpenStart(GameObject avatar)
        {
            Open(null);
            GetWindow<ExpressionEditorWindow>().BeginStart(avatar);
        }

        public static void Open(FacialExpressionAvatar avatar)
        {
            var window = GetWindow<ExpressionEditorWindow>();
            window.titleContent = new GUIContent("表情エディタ");
            window.minSize = new Vector2(980, 560);
            if (avatar != null) window.SetAvatar(avatar);
            window.Show();
        }

        private void OnEnable()
        {
            _thumbnails = new ThumbnailCache();
            _tab = (Tab)Mathf.Clamp((int)_tab, 0, TabLabels.Length - 1);
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += OnUndoRedo;
            ExpressionClipEditorWindow.Edited += OnClipEdited;
            if (_avatar == null) _avatar = Setups.FirstOrDefault();
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            ExpressionClipEditorWindow.Edited -= OnClipEdited;
            DisposePreview();
            _thumbnails?.Dispose();
        }

        private void OnHierarchyChanged()
        {
            _setups = null;
            Repaint();
        }

        private void OnProjectChanged()
        {
            _setups = null;
            _startInfo = null;
            MarkLibraryDirty();
            InvalidateDetailPreview();
            _faceDefaults = null;
            Repaint();
        }

        private void OnUndoRedo()
        {
            InvalidateDetailPreview();
            _faceDefaults = null;
            Repaint();
        }

        /// <summary>
        /// 表情の編集ウィンドウで表情を編集したら、プレビューを描き直す（サムネイルはキーが変わるので描き直される）。
        /// </summary>
        private void OnClipEdited()
        {
            InvalidateDetailPreview();
            MarkLibraryDirty();
            Repaint();
        }

        /// <summary>
        /// サムネイルはGUIの外で少しずつ描画する。
        /// </summary>
        private void OnEditorUpdate()
        {
            UpdateAnimationPreview();
            if (_thumbnails == null || !_thumbnails.HasPending || _preview == null) return;
            _thumbnails.ProcessPending(4);
            Repaint();
        }

        private void SetAvatar(FacialExpressionAvatar avatar)
        {
            _starting = false;
            if (_avatar == avatar) return;
            _avatar = avatar;
            _faceDefaults = null;
            DisposePreview();
            MarkLibraryDirty();
            Select(SelectionKind.None, null, null);
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_starting || _avatar == null)
            {
                DrawStart();
                return;
            }

            if (AvatarRoot == null)
            {
                DrawMissingAvatar();
                return;
            }

            var set = Set;
            if (set == null)
            {
                EditorGUILayout.HelpBox("この表情設定には表情データがありません。「新しく始める」で作ってください。", MessageType.Info);
                if (GUILayout.Button("新しく始める", GUILayout.Width(160))) BeginStart(AvatarRoot);
                return;
            }

            // 旧形式（表情セットと固定メニューが別）のデータなら、表情メニューへ移す。
            if (Event.current.type == EventType.Layout && set.menu.Count == 0 && (set.gestureSets.Count > 0 || set.fixedMenu.Count > 0))
            {
                Modify(set, "表情メニューを作成", () => ExpressionSetUtility.EnsureMenu(set));
            }

            // この機能より前に作った表情データは、表情ごとのまばたき・リップシンクを元FXから一度だけ取り込む。
            if (Event.current.type == EventType.Layout && !set.faceControlImported)
            {
                var changed = 0;
                Modify(set, "まばたき・リップシンクの設定を取り込み", () =>
                {
                    changed = FxImporter.ImportFaceControl(Descriptor, set);
                    set.faceControlImported = true;
                });
                if (changed > 0) ShowNotification(new GUIContent($"元FXから、{changed} 件の表情のまばたき・リップシンクの設定を取り込みました"), 4);
            }

            EnsurePreview();
            RefreshLibraryIfNeeded(set);

            // 使用状況と絞り込みはレイアウトのときだけ計算し直す（IMGUIはイベントごとにOnGUIが呼ばれるため）。
            if (Event.current.type == EventType.Layout || _usage == null)
            {
                _usage = ClipUsage.Compute(set);
                _filteredClips = FilteredClips();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (var library = new EditorGUILayout.VerticalScope(GUILayout.Width(LibraryWidth)))
                {
                    DrawLibrary(set);
                    AcceptFolderDrop(library.rect, set);
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    _tab = (Tab)GUILayout.Toolbar((int)_tab, TabLabels, GUILayout.Height(24));
                    _centerScroll = EditorGUILayout.BeginScrollView(_centerScroll);
                    switch (_tab)
                    {
                        case Tab.Menu: DrawMenuTab(set); break;
                        case Tab.Parts: DrawPartsTab(set); break;
                        case Tab.Face: DrawFaceTab(set); break;
                        case Tab.AfkContacts: DrawAfkContactsTab(set); break;
                        case Tab.Setup: DrawSetupTab(set); break;
                    }
                    EditorGUILayout.EndScrollView();
                }

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(DetailWidth)))
                {
                    DrawDetail(set);
                }
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var setups = Setups;
                var index = _starting ? -1 : setups.IndexOf(_avatar);
                var labels = setups.Select(AvatarSetup.DisplayName).ToArray();
                GUILayout.Label("表情設定", GUILayout.Width(56));
                var next = EditorGUILayout.Popup(index, labels, EditorStyles.toolbarPopup, GUILayout.Width(240));
                if (next != index && next >= 0) SetAvatar(setups[next]);
                if (GUILayout.Button("新しく始める", EditorStyles.toolbarButton)) BeginStart(null);

                if (_avatar != null && !_starting)
                {
                    GUILayout.Space(8);
                    GUILayout.Label($"表情データ：{(Set != null ? Set.name : "なし")}", EditorStyles.miniLabel);
                    GUILayout.Space(8);
                    GUILayout.Label($"ベース顔：{(Variant != null ? Variant.name : "シーンの顔")}", EditorStyles.miniLabel);
                }

                GUILayout.FlexibleSpace();

                if (_avatar != null && !_starting)
                {
                    DrawSetupHandle();
                    using (new EditorGUI.DisabledScope(Set == null))
                    {
                        if (GUILayout.Button(new GUIContent("クリップを合成", "目元・口元のクリップから完成表情を作ります。"),
                                EditorStyles.toolbarButton))
                        {
                            ExpressionComposerWindow.Open(Set, null);
                        }
                    }
                }
                if (GUILayout.Button("表示を更新", EditorStyles.toolbarButton))
                {
                    _setups = null;
                    DisposePreview();
                    _thumbnails.Clear();
                    MarkLibraryDirty();
                }
            }
        }

        /// <summary>
        /// 表情設定のプレハブ（ドラッグしてアバターへ入れられる）か、シーンの表情設定を選ぶボタン。
        /// </summary>
        private void DrawSetupHandle()
        {
            var inScene = AvatarSetup.IsInScene(_avatar);
            var content = inScene
                ? new GUIContent("表情設定を選択", "Hierarchy で表情設定を選びます。")
                : new GUIContent($"プレハブ：{_avatar.gameObject.name}", "ドラッグして Hierarchy のアバターの中に入れます。クリックで Project に表示します。");
            var rect = GUILayoutUtility.GetRect(content, EditorStyles.toolbarButton, GUILayout.MaxWidth(260));
            if (!inScene)
            {
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Pan);
                HandleDragSource(rect, _avatar.gameObject);
            }
            if (GUI.Button(rect, content, EditorStyles.toolbarButton))
            {
                Selection.activeObject = _avatar.gameObject;
                EditorGUIUtility.PingObject(_avatar.gameObject);
            }
        }

        private void ImportFromFx()
        {
            var set = Set;
            if (!EditorUtility.DisplayDialog("元FXから取り込む",
                    "元FXのジェスチャーで出している表情を、表情セット（ジェスチャーの割り当て）に取り込みます。\n" +
                    "表情セットと置き換える元FXレイヤーは、元FXの内容で設定し直します（同じクリップの表情の設定は残ります）。",
                    "取り込む", "キャンセル"))
            {
                return;
            }

            FxImporter.Result result = null;
            Modify(set, "元FXから取り込み", () => result = FxImporter.ImportGestures(Descriptor, set));
            MarkLibraryDirty();
            EditorUtility.DisplayDialog("元FXから取り込む", result.Summary(), "OK");
        }

        // ---- プレビューとサムネイル ----

        private void EnsurePreview()
        {
            var root = AvatarRoot;
            if (_preview != null && _preview.Source == root) return;
            DisposePreview();
            _preview = new FacePreview(root);
            _faceHash = ThumbnailKeys.FaceHash(root);

            // 表情セットのメニューのアイコン（無表情）に使うので、無表情のサムネイルも描いておく。
            _thumbnails.Get(ThumbnailKeys.Neutral(_faceHash), () =>
            {
                _preview.Apply(null);
                return _preview.RenderStatic(ThumbnailCache.Size);
            });
        }

        private void DisposePreview()
        {
            InvalidateDetailPreview();
            ReleaseDetailTexture();
            _preview?.Dispose();
            _preview = null;
        }

        private Texture2D ClipThumbnail(AnimationClip clip)
        {
            if (clip == null) return null;
            return _thumbnails.Get(ThumbnailKeys.Clip(clip, _faceHash), () =>
            {
                _preview.Apply(clip);
                return _preview.RenderStatic(ThumbnailCache.Size);
            });
        }

        private Texture2D ExpressionThumbnail(Expression expression)
        {
            if (expression == null) return null;
            var variant = Variant;
            return _thumbnails.Get(ThumbnailKeys.Expression(expression, variant, _faceHash), () =>
            {
                var clip = PreviewClips.ForExpression(expression, variant, AvatarRoot);
                _preview.Apply(clip);
                PreviewClips.Release(clip);
                return _preview.RenderStatic(ThumbnailCache.Size);
            });
        }

        private Texture2D PartThumbnail(FacialPart part)
        {
            if (part == null) return null;
            return _thumbnails.Get(ThumbnailKeys.Part(part, _faceHash), () =>
            {
                var clip = PreviewClips.ForPart(part);
                _preview.Apply(null);
                _preview.Overlay(clip);
                PreviewClips.Release(clip);
                return _preview.RenderStatic(ThumbnailCache.Size);
            });
        }
        // ---- 選択 ----

        private void Select(SelectionKind kind, string id, AnimationClip clip, Hand hand = default)
        {
            _selectionKind = kind;
            _selectedId = id;
            _selectedClip = clip;
            _selectedHand = hand;
            InvalidateDetailPreview();
            GUI.FocusControl(null);
        }

        private bool IsSelected(SelectionKind kind, string id, AnimationClip clip = null, Hand hand = default)
        {
            if (_selectionKind != kind) return false;
            if (kind == SelectionKind.Clip) return _selectedClip == clip;
            if (kind == SelectionKind.OriginalClip) return _selectedClip == clip && _selectedId == id;
            return _selectedId == id && (kind != SelectionKind.Fist || _selectedHand == hand);
        }

        // ---- 編集 ----

        /// <summary>
        /// 表情データを変更する。Undoに記録し、保存対象にする。
        /// </summary>
        private void Modify(ExpressionSet set, string undoName, System.Action change)
        {
            Undo.RecordObject(set, undoName);
            change();
            EditorUtility.SetDirty(set);
            InvalidateDetailPreview();
            _faceDefaults = null;
        }

        // ---- ドラッグ＆ドロップ ----

        private Object _dragCandidate;

        /// <summary>
        /// rect 内でドラッグを始めたら、クリップ（や表情設定のプレハブ）をドラッグする。
        /// </summary>
        private void HandleDragSource(Rect rect, Object clip)
        {
            var e = Event.current;
            if (clip == null) return;

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _dragCandidate = clip;
            }
            else if (e.type == EventType.MouseDrag && _dragCandidate == clip && rect.Contains(e.mousePosition))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new Object[] { clip };
                DragAndDrop.StartDrag(clip.name);
                _dragCandidate = null;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                _dragCandidate = null;
            }
        }

        /// <summary>
        /// rect にクリップがドロップされたら返す。ライブラリとプロジェクトウィンドウのどちらからでも受け付ける。
        /// </summary>
        private static AnimationClip AcceptClipDrop(Rect rect)
        {
            var e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return null;
            if (!rect.Contains(e.mousePosition)) return null;

            var clip = DragAndDrop.objectReferences.OfType<AnimationClip>().FirstOrDefault();
            if (clip == null) return null;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                e.Use();
                return clip;
            }
            e.Use();
            return null;
        }

        // ---- 描画の部品 ----

        private static GUIStyle _cellLabel;
        private static GUIStyle CellLabel => _cellLabel ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.UpperCenter,
            wordWrap = false,
            clipping = TextClipping.Clip,
        };

        private static GUIStyle _warningMiniLabel;
        private static GUIStyle WarningMiniLabel => _warningMiniLabel ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.95f, 0.65f, 0.2f) },
        };

        private static GUIStyle _warningBoldLabel;
        private static GUIStyle WarningBoldLabel => _warningBoldLabel ??= new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = new Color(0.95f, 0.65f, 0.2f) },
        };

        private static GUIStyle _dropHint;
        private static GUIStyle DropHint => _dropHint ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };

        /// <summary>
        /// サムネイルと名前のセルを描く。戻り値はクリックされたかどうか。
        /// </summary>
        private bool DrawCell(Rect rect, Texture thumbnail, string label, bool selected, string badge = null, Color? badgeColor = null)
        {
            if (Event.current.type == EventType.Repaint)
            {
                var background = selected ? new Color(0.24f, 0.48f, 0.9f, 0.45f) : new Color(0, 0, 0, 0.18f);
                EditorGUI.DrawRect(rect, background);
            }

            var thumbSize = Mathf.Min(rect.width - 6, rect.height - 18);
            var thumbRect = new Rect(rect.x + (rect.width - thumbSize) / 2, rect.y + 3, thumbSize, thumbSize);
            if (thumbnail != null)
            {
                GUI.DrawTexture(thumbRect, thumbnail, ScaleMode.ScaleToFit);
            }
            else if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(thumbRect, new Color(0, 0, 0, 0.25f));
            }

            if (badge != null)
            {
                var badgeRect = new Rect(thumbRect.x, thumbRect.y, thumbRect.width, 14);
                if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(badgeRect, badgeColor ?? new Color(0.85f, 0.55f, 0.1f, 0.85f));
                GUI.Label(badgeRect, badge, CellLabel);
            }

            GUI.Label(new Rect(rect.x + 2, thumbRect.yMax + 1, rect.width - 4, 15), label, CellLabel);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                GUI.changed = true;
                Repaint();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 横幅に合わせてセルを並べるための矩形を返す。
        /// </summary>
        private static List<Rect> GridRects(int count, float cellWidth, float cellHeight, float availableWidth)
        {
            var columns = Mathf.Max(1, Mathf.FloorToInt((availableWidth + 4) / (cellWidth + 4)));
            var rows = Mathf.CeilToInt(count / (float)columns);
            var area = GUILayoutUtility.GetRect(availableWidth, rows * (cellHeight + 4));
            var rects = new List<Rect>(count);
            for (var i = 0; i < count; i++)
            {
                var column = i % columns;
                var row = i / columns;
                rects.Add(new Rect(area.x + column * (cellWidth + 4), area.y + row * (cellHeight + 4), cellWidth, cellHeight));
            }
            return rects;
        }

        private float CenterWidth => Mathf.Max(200, position.width - LibraryWidth - DetailWidth - 30);
    }
}
