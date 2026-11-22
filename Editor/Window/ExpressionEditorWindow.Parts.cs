using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 「パーツ」タブ。ゲーム中にメニューから表情へ重ねて出すパーツ（汗・涙・頬染めなど）。
    /// - 「＋ 新しいパーツ」で空のパーツを作り、表情の編集ウィンドウで編集する。クリップのドロップでも追加できる
    /// - パーツはドラッグで並べ替えられる（ゲーム内のパーツメニューの並び順になる）
    /// </summary>
    internal partial class ExpressionEditorWindow
    {
        private const float MenuCellWidth = 86f;
        private const float MenuCellHeight = 102f;
        private const string PartDragKey = "FacialExpressionEditor.Part";
        private const string PartCategoryDragKey = "FacialExpressionEditor.PartCategory";
        private string _partCategoryDragCandidate;
        private Vector2 _partCategoryDragStart;
        private string _partCategoryDropBefore;
        private static readonly Color DropMarkerColor = new Color(0.3f, 0.6f, 1f);

        private string _partDragCandidate;
        // ドラッグ中に、どのパーツの前に入れるか（"" なら最後）。ドラッグしていなければ null。
        private string _partDropBefore;
        private string _partDropFolderKey;
        private readonly HashSet<string> _closedPartFolders = new HashSet<string>();
        private string _partFolderDraft = "";
        private string _renamingPartFolder;

        private void DrawPartsTab(ExpressionSet set)
        {
            EditorGUILayout.HelpBox(
                "ゲーム中にメニューから表情へ重ねて出すパーツです（汗・涙・頬染めなど）。「＋ 新しいパーツ」で作るか、クリップをドロップして追加します。\n" +
                "パーツを見出しへドラッグすると移動、パーツの上なら直前へ挿入します。見出し同士のドラッグで分類順を変更できます。", MessageType.None);

            if (Event.current.type == EventType.DragExited) { _partDropBefore = null; _partDropFolderKey = null; _partCategoryDropBefore = null; }

            using (new EditorGUILayout.HorizontalScope())
            {
                _partFolderDraft = EditorGUILayout.TextField(string.IsNullOrEmpty(_renamingPartFolder) ? "フォルダ名" : "新しい名前", _partFolderDraft);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_partFolderDraft)))
                if (GUILayout.Button(string.IsNullOrEmpty(_renamingPartFolder) ? "フォルダを作成" : "名前を変更", GUILayout.Width(100)))
                {
                    var name = _partFolderDraft.Trim();
                    if (!RenamePartFolder(set, _renamingPartFolder, name))
                        ShowNotification(new GUIContent("重複しない1階層の名前を入力してください"));
                    else
                    {
                        _renamingPartFolder = null;
                        _partFolderDraft = "";
                    }
                }
                if (!string.IsNullOrEmpty(_renamingPartFolder) && GUILayout.Button("取消", GUILayout.Width(45)))
                { _renamingPartFolder = null; _partFolderDraft = ""; }
            }
            DrawPartGrid(set, set.parts.Where(p => string.IsNullOrEmpty(p.exclusiveGroup) && string.IsNullOrEmpty(p.folder)).ToList(), "");
            foreach (var key in PartCategoryOrder.Keys(set))
            {
                var exclusive = key.StartsWith("exclusive:");
                DrawPartFolder(set, key.Substring(exclusive ? 10 : 7), exclusive,
                    set.parts.Where(p => PartCategoryOrder.Key(p) == key).ToList());
            }
            var endRect = EditorGUILayout.GetControlRect(false, 18);
            HandlePartCategoryDrop(set, endRect, "");
            if (DragAndDrop.GetGenericData(PartCategoryDragKey) != null)
                GUI.Label(endRect, "ここへドロップして末尾へ", EditorStyles.centeredGreyMiniLabel);
        }

        private static List<string> PartFolders(ExpressionSet set) => set.partFolders
            .Concat(set.parts.Select(p => p.folder)).Where(f => !string.IsNullOrEmpty(f)).Distinct().ToList();

        private bool RenamePartFolder(ExpressionSet set, string oldName, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Contains("/") || name.Contains("\\") ||
                (name != oldName && PartFolders(set).Contains(name))) return false;
            if (name == oldName) return true;
            Modify(set, "パーツフォルダを変更", () =>
            {
                var order = PartCategoryOrder.Keys(set);
                var index = order.IndexOf("folder:" + oldName);
                if (index >= 0) order[index] = "folder:" + name;
                else order.Add("folder:" + name);
                set.partCategoryOrder = order;
                if (!string.IsNullOrEmpty(oldName))
                {
                    set.partFolders.Remove(oldName);
                    foreach (var part in set.parts.Where(p => p.folder == oldName)) part.folder = name;
                }
                set.partFolders.Add(name);
            });
            if (!string.IsNullOrEmpty(oldName) && _closedPartFolders.Remove("folder:" + oldName))
                _closedPartFolders.Add("folder:" + name);
            return true;
        }

        private void RemovePartFolder(ExpressionSet set, string folder)
        {
            Modify(set, "パーツフォルダを解除", () =>
            {
                set.partFolders.Remove(folder);
                set.partCategoryOrder?.Remove("folder:" + folder);
                foreach (var part in set.parts.Where(p => p.folder == folder)) part.folder = "";
            });
            _closedPartFolders.Remove("folder:" + folder);
            if (_renamingPartFolder == folder) { _renamingPartFolder = null; _partFolderDraft = ""; }
        }

        private void DrawPartFolder(ExpressionSet set, string folder, bool exclusive, List<FacialPart> parts)
        {
            var key = (exclusive ? "exclusive:" : "folder:") + folder;
            var rect = EditorGUILayout.GetControlRect();
            HandlePartCategoryDrag(set, rect, key);
            HandlePartCategoryDrop(set, rect, key);
            var open = !_closedPartFolders.Contains(key);
            // Handle drops before Foldout consumes drag events for its own hover behavior.
            HandlePartDrop(set, rect, null, exclusive ? null : folder, exclusive ? folder : null);
            var next = EditorGUI.Foldout(rect, open, $"{folder} ({parts.Count}){(exclusive ? "　排他グループ" : "")}", false);
            if (next) _closedPartFolders.Remove(key); else _closedPartFolders.Add(key);
            if (_partDropBefore == "" && _partDropFolderKey == key) DrawDropMarker(rect);
            if (!exclusive && Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("名前変更"), false, () => { _renamingPartFolder = folder; _partFolderDraft = folder; Repaint(); });
                menu.AddItem(new GUIContent("フォルダを解除（パーツは残す）"), false, () =>
                    RemovePartFolder(set, folder));
                menu.ShowAsContext(); Event.current.Use();
            }
            if (next) DrawPartGrid(set, parts, exclusive ? null : folder);
        }

        private bool MovePartCategory(ExpressionSet set, string key, string before)
        {
            var order = PartCategoryOrder.Keys(set);
            if (!order.Contains(key) || key == before || (before != "" && !order.Contains(before))) return false;
            var previous = order.ToList();
            order.Remove(key);
            order.Insert(before == "" ? order.Count : order.IndexOf(before), key);
            if (order.SequenceEqual(previous)) return false;
            Modify(set, "パーツ分類を並べ替え", () => set.partCategoryOrder = order);
            Repaint();
            return true;
        }

        private void HandlePartCategoryDrag(ExpressionSet set, Rect rect, string key)
        {
            var e = Event.current;
            // The arrow keeps standard foldout handling; the label supports click and drag.
            var label = new Rect(rect.x + 16, rect.y, rect.width - 16, rect.height);
            if (e.type == EventType.MouseDown && e.button == 0 && label.Contains(e.mousePosition))
            {
                _partCategoryDragCandidate = key;
                _partCategoryDragStart = e.mousePosition;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _partCategoryDragCandidate == key &&
                     (e.mousePosition - _partCategoryDragStart).sqrMagnitude >= 16)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(PartDragKey, null);
                DragAndDrop.SetGenericData(PartCategoryDragKey, key);
                DragAndDrop.objectReferences = new Object[] { set };
                GUIUtility.hotControl = 0;
                DragAndDrop.StartDrag(key);
                _partCategoryDragCandidate = null;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _partCategoryDragCandidate == key)
            {
                _partCategoryDragCandidate = null;
                if (label.Contains(e.mousePosition))
                {
                    if (!_closedPartFolders.Remove(key)) _closedPartFolders.Add(key);
                    Repaint();
                }
                e.Use();
            }
        }

        private void HandlePartCategoryDrop(ExpressionSet set, Rect rect, string before)
        {
            var e = Event.current;
            if (_partCategoryDropBefore == before && e.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), DropMarkerColor);
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !rect.Contains(e.mousePosition)) return;
            var key = DragAndDrop.GetGenericData(PartCategoryDragKey) as string;
            if (key == null) return;
            var valid = DragAndDrop.objectReferences.Length == 1 && DragAndDrop.objectReferences[0] == set &&
                        key != before && PartCategoryOrder.Keys(set).Contains(key);
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
            _partCategoryDropBefore = valid ? before : null;
            var perform = e.type == EventType.DragPerform;
            e.Use(); Repaint();
            if (!perform || !valid) return;
            DragAndDrop.AcceptDrag();
            DragAndDrop.SetGenericData(PartCategoryDragKey, null);
            _partCategoryDropBefore = null;
            if (MovePartCategory(set, key, before)) GUIUtility.ExitGUI();
        }

        private void DrawPartGrid(ExpressionSet set, List<FacialPart> parts, string folder)
        {
            var rects = GridRects(parts.Count + (folder != null ? 1 : 0), MenuCellWidth, MenuCellHeight, CenterWidth);
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var rect = rects[i];
                if (Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition))
                {
                    var menu = new GenericMenu();
                    if (!string.IsNullOrEmpty(part.exclusiveGroup))
                        menu.AddDisabledItem(new GUIContent("排他グループで分類中（所属は詳細で変更）"));
                    else
                    {
                        menu.AddItem(new GUIContent("移動/フォルダなし"), string.IsNullOrEmpty(part.folder),
                            () => Modify(set, "パーツを移動", () => part.folder = ""));
                        foreach (var destination in PartFolders(set))
                            menu.AddItem(new GUIContent("移動/" + destination), part.folder == destination,
                                () => Modify(set, "パーツを移動", () => part.folder = destination));
                    }
                    menu.ShowAsContext(); Event.current.Use();
                }
                var group = string.IsNullOrEmpty(part.exclusiveGroup) ? null : part.exclusiveGroup;
                var removeRect = new Rect(rect.xMax - 18, rect.y + 2, 16, 16);
                if (!removeRect.Contains(Event.current.mousePosition)) HandlePartDrag(rect, part);
                if (DrawCell(rect, PartThumbnail(part), part.name, IsSelected(SelectionKind.Part, part.id), group,
                        new Color(0.3f, 0.45f, 0.75f, 0.85f)))
                {
                    Select(SelectionKind.Part, part.id, null);
                }
                HandlePartDrop(set, rect, part, folder, group);
                if (_partDropBefore == part.id) DrawDropMarker(rect);

                if (GUI.Button(removeRect, "×", EditorStyles.miniButton) &&
                    EditorUtility.DisplayDialog("パーツを削除", $"「{part.name}」を削除しますか？", "削除", "キャンセル"))
                {
                    Modify(set, "パーツを削除", () => set.parts.Remove(part));
                    GUIUtility.ExitGUI();
                }
            }

            if (folder == null) return;
            var addRect = rects[parts.Count];
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(addRect, new Color(0, 0, 0, 0.12f));
            HandlePartDrop(set, addRect, null, folder, null);
            if (_partDropBefore == "" && _partDropFolderKey == "folder:" + folder) DrawDropMarker(addRect);
            if (GUI.Button(new Rect(addRect.x + 4, addRect.y + 14, addRect.width - 8, 38), "＋ 新しい\nパーツ"))
            {
                CreatePart(set, folder);
                GUIUtility.ExitGUI();
            }
            GUI.Label(new Rect(addRect.x, addRect.y + 52, addRect.width, 40), "クリップを\nドロップ", DropHint);
            var dropped = AcceptClipDrop(addRect);
            if (dropped != null)
            {
                var part = new FacialPart
                {
                    name = dropped.name,
                    folder = folder,
                    clip = dropped,
                    properties = FxImporter.DefaultPartProperties(dropped),
                };
                Modify(set, "パーツを追加", () => set.parts.Add(part));
                ExpressionSetUtility.MakePartClipEditable(set, part);
                Select(SelectionKind.Part, part.id, null);
                MarkLibraryDirty();
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>
        /// パーツのセルをドラッグし始めたら、並べ替えのドラッグにする。
        /// </summary>
        private void HandlePartDrag(Rect rect, FacialPart part)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _partDragCandidate = part.id;
            }
            else if (e.type == EventType.MouseDrag && _partDragCandidate == part.id && rect.Contains(e.mousePosition))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(PartCategoryDragKey, null);
                DragAndDrop.SetGenericData(PartDragKey, part.id);
                DragAndDrop.objectReferences = new Object[0];
                GUIUtility.hotControl = 0;
                DragAndDrop.StartDrag(part.name);
                _partDragCandidate = null;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                _partDragCandidate = null;
            }
        }

        /// <summary>
        /// ドラッグしてきたパーツを、target の前（null なら移動先分類の最後）に入れる。
        /// </summary>
        private void HandlePartDrop(ExpressionSet set, Rect rect, FacialPart target, string folder, string exclusiveGroup)
        {
            var e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!rect.Contains(e.mousePosition)) return;

            var draggedId = DragAndDrop.GetGenericData(PartDragKey) as string;
            var dragged = draggedId != null ? set.parts.Find(p => p.id == draggedId) : null;
            if (dragged == null) return;

            if (!CanMovePart(set, dragged, target, folder, exclusiveGroup))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                _partDropBefore = null;
                _partDropFolderKey = null;
                e.Use();
                Repaint();
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            if (e.type == EventType.DragUpdated)
            {
                var before = target != null ? target.id : "";
                var key = folder != null ? "folder:" + folder : "exclusive:" + exclusiveGroup;
                if (_partDropBefore != before || _partDropFolderKey != key)
                {
                    _partDropBefore = before;
                    _partDropFolderKey = key;
                    Repaint();
                }
                e.Use();
                return;
            }

            DragAndDrop.AcceptDrag();
            e.Use();
            _partDropBefore = null;
            _partDropFolderKey = null;
            DragAndDrop.SetGenericData(PartDragKey, null);
            if (MovePart(set, dragged, target, folder, exclusiveGroup)) GUIUtility.ExitGUI();
        }

        private static bool CanMovePart(ExpressionSet set, FacialPart dragged, FacialPart target, string folder, string exclusiveGroup)
        {
            if (dragged == null || !set.parts.Contains(dragged) || target == dragged) return false;
            if (folder != null)
            {
                if (!string.IsNullOrEmpty(dragged.exclusiveGroup) ||
                    (folder.Length > 0 && !PartFolders(set).Contains(folder))) return false;
                return target == null || set.parts.Contains(target) && string.IsNullOrEmpty(target.exclusiveGroup) && (target.folder ?? "") == folder;
            }
            return !string.IsNullOrEmpty(exclusiveGroup) && dragged.exclusiveGroup == exclusiveGroup &&
                (target == null || set.parts.Contains(target) && target.exclusiveGroup == exclusiveGroup);
        }

        private bool MovePart(ExpressionSet set, FacialPart dragged, FacialPart target, string folder, string exclusiveGroup)
        {
            if (!CanMovePart(set, dragged, target, folder, exclusiveGroup)) return false;
            Modify(set, "パーツを移動・並べ替え", () =>
            {
                set.parts.Remove(dragged);
                if (folder != null) dragged.folder = folder;
                var index = target != null ? set.parts.IndexOf(target) : set.parts.FindLastIndex(p => folder != null
                    ? string.IsNullOrEmpty(p.exclusiveGroup) && (p.folder ?? "") == folder
                    : p.exclusiveGroup == exclusiveGroup) + 1;
                if (target == null && index == 0) index = set.parts.Count;
                set.parts.Insert(index < 0 ? set.parts.Count : index, dragged);
            });
            Repaint();
            return true;
        }

        // 入れる位置の印（セルの左端の縦線）。
        private static void DrawDropMarker(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(new Rect(rect.x - 3, rect.y, 3, rect.height), DropMarkerColor);
        }

        /// <summary>
        /// 空のパーツを作り、表情の編集ウィンドウで開く。
        /// </summary>
        private void CreatePart(ExpressionSet set, string folder = "")
        {
            var name = "新しいパーツ";
            for (var i = 2; set.parts.Any(p => p.name == name); i++) name = $"新しいパーツ {i}";

            var part = ExpressionSetUtility.CreatePart(set, name);
            part.folder = folder;
            Select(SelectionKind.Part, part.id, null);
            ExpressionClipEditorWindow.OpenPart(_avatar, part);
        }
    }
}
