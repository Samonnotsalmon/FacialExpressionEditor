using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 「パーツ」タブ。ゲーム中にメニューから表情へ重ねて出すパーツ（汗・涙・頬染めなど）。
    /// - 「＋ 新しいパーツ」で空のパーツを作り、表情の編集ウィンドウで編集する。クリップのドロップでも追加できる
    /// </summary>
    internal partial class ExpressionEditorWindow
    {
        private const float MenuCellWidth = 86f;
        private const float MenuCellHeight = 102f;

        private void DrawPartsTab(ExpressionSet set)
        {
            EditorGUILayout.HelpBox(
                "ゲーム中にメニューから表情へ重ねて出すパーツです（汗・涙・頬染めなど）。「＋ 新しいパーツ」で作るか、クリップをドロップして追加します。\n" +
                "動かすプロパティの細かい選択は、表情データのインスペクタで行います。", MessageType.None);

            var rects = GridRects(set.parts.Count + 1, MenuCellWidth, MenuCellHeight, CenterWidth);
            for (var i = 0; i < set.parts.Count; i++)
            {
                var part = set.parts[i];
                var rect = rects[i];
                var group = string.IsNullOrEmpty(part.exclusiveGroup) ? null : part.exclusiveGroup;
                if (DrawCell(rect, PartThumbnail(part), part.name, IsSelected(SelectionKind.Part, part.id), group,
                        new Color(0.3f, 0.45f, 0.75f, 0.85f)))
                {
                    Select(SelectionKind.Part, part.id, null);
                }

                if (GUI.Button(new Rect(rect.xMax - 18, rect.y + 2, 16, 16), "×", EditorStyles.miniButton) &&
                    EditorUtility.DisplayDialog("パーツを削除", $"「{part.name}」を削除しますか？", "削除", "キャンセル"))
                {
                    Modify(set, "パーツを削除", () => set.parts.Remove(part));
                    GUIUtility.ExitGUI();
                }
            }

            var addRect = rects[set.parts.Count];
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(addRect, new Color(0, 0, 0, 0.12f));
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
