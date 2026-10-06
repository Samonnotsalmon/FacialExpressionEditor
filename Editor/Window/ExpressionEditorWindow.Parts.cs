using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private const float MenuCellWidth = 86f;
        private const float MenuCellHeight = 102f;

        private void DrawPartsTab(ExpressionSet set)
        {
            EditorGUILayout.HelpBox(
                "ゲーム中にメニューから表情へ重ねて出すパーツです（汗・涙・頬染めなど）。クリップをドロップすると追加します。\n" +
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

            var dropRect = rects[set.parts.Count];
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(dropRect, new Color(0, 0, 0, 0.12f));
            GUI.Label(dropRect, "ここに\nドロップ", DropHint);
            var dropped = AcceptClipDrop(dropRect);
            if (dropped != null)
            {
                var part = new FacialPart
                {
                    name = dropped.name,
                    clip = dropped,
                    properties = FxImporter.DefaultPartProperties(dropped),
                };
                Modify(set, "パーツを追加", () => set.parts.Add(part));
                Select(SelectionKind.Part, part.id, null);
                MarkLibraryDirty();
                GUIUtility.ExitGUI();
            }
        }
    }
}
