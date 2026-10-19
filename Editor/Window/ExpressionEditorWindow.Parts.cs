using System.Linq;
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
        private static readonly Color DropMarkerColor = new Color(0.3f, 0.6f, 1f);

        private string _partDragCandidate;
        // ドラッグ中に、どのパーツの前に入れるか（"" なら最後）。ドラッグしていなければ null。
        private string _partDropBefore;

        private void DrawPartsTab(ExpressionSet set)
        {
            EditorGUILayout.HelpBox(
                "ゲーム中にメニューから表情へ重ねて出すパーツです（汗・涙・頬染めなど）。「＋ 新しいパーツ」で作るか、クリップをドロップして追加します。\n" +
                "ドラッグで並べ替えると、ゲーム内のパーツメニューもその順に並びます。", MessageType.None);

            if (Event.current.type == EventType.DragExited) _partDropBefore = null;

            var rects = GridRects(set.parts.Count + 1, MenuCellWidth, MenuCellHeight, CenterWidth);
            for (var i = 0; i < set.parts.Count; i++)
            {
                var part = set.parts[i];
                var rect = rects[i];
                var group = string.IsNullOrEmpty(part.exclusiveGroup) ? null : part.exclusiveGroup;
                var removeRect = new Rect(rect.xMax - 18, rect.y + 2, 16, 16);
                if (!removeRect.Contains(Event.current.mousePosition)) HandlePartDrag(rect, part);
                if (DrawCell(rect, PartThumbnail(part), part.name, IsSelected(SelectionKind.Part, part.id), group,
                        new Color(0.3f, 0.45f, 0.75f, 0.85f)))
                {
                    Select(SelectionKind.Part, part.id, null);
                }
                HandlePartDrop(set, rect, part);
                if (_partDropBefore == part.id) DrawDropMarker(rect);

                if (GUI.Button(removeRect, "×", EditorStyles.miniButton) &&
                    EditorUtility.DisplayDialog("パーツを削除", $"「{part.name}」を削除しますか？", "削除", "キャンセル"))
                {
                    Modify(set, "パーツを削除", () => set.parts.Remove(part));
                    GUIUtility.ExitGUI();
                }
            }

            var addRect = rects[set.parts.Count];
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(addRect, new Color(0, 0, 0, 0.12f));
            HandlePartDrop(set, addRect, null);
            if (_partDropBefore == "") DrawDropMarker(addRect);
            if (GUI.Button(new Rect(addRect.x + 4, addRect.y + 14, addRect.width - 8, 38), "＋ 新しい\nパーツ"))
            {
                CreatePart(set);
                GUIUtility.ExitGUI();
            }
            GUI.Label(new Rect(addRect.x, addRect.y + 52, addRect.width, 40), "クリップを\nドロップ", DropHint);
            var dropped = AcceptClipDrop(addRect);
            if (dropped != null)
            {
                var part = new FacialPart
                {
                    name = dropped.name,
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
                DragAndDrop.SetGenericData(PartDragKey, part.id);
                DragAndDrop.objectReferences = new Object[0];
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
        /// ドラッグしてきたパーツを、target の前（null なら最後）に入れる。
        /// </summary>
        private void HandlePartDrop(ExpressionSet set, Rect rect, FacialPart target)
        {
            var e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!rect.Contains(e.mousePosition)) return;

            var draggedId = DragAndDrop.GetGenericData(PartDragKey) as string;
            var dragged = draggedId != null ? set.parts.Find(p => p.id == draggedId) : null;
            if (dragged == null) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            if (e.type == EventType.DragUpdated)
            {
                var before = target != null ? target.id : "";
                if (_partDropBefore != before)
                {
                    _partDropBefore = before;
                    Repaint();
                }
                e.Use();
                return;
            }

            DragAndDrop.AcceptDrag();
            e.Use();
            _partDropBefore = null;
            if (dragged == target) return;
            Modify(set, "パーツを並べ替え", () =>
            {
                set.parts.Remove(dragged);
                var index = target != null ? set.parts.IndexOf(target) : set.parts.Count;
                set.parts.Insert(index < 0 ? set.parts.Count : index, dragged);
            });
            GUIUtility.ExitGUI();
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
        private void CreatePart(ExpressionSet set)
        {
            var name = "新しいパーツ";
            for (var i = 2; set.parts.Any(p => p.name == name); i++) name = $"新しいパーツ {i}";

            var part = ExpressionSetUtility.CreatePart(set, name);
            Select(SelectionKind.Part, part.id, null);
            ExpressionClipEditorWindow.OpenPart(_avatar, part);
        }
    }
}
