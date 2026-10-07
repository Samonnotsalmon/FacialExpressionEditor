using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private const float MenuRowHeight = 40f;
        private const float MenuIndent = 18f;
        private const string MenuNodeDragKey = "FacialExpressionEditor.MenuNode";

        [SerializeField] private string _selectedNodeId;
        [SerializeField] private bool _showSwitchEffect;
        private readonly HashSet<string> _collapsedFolders = new HashSet<string>();
        private string _menuDragCandidate;

        /// <summary>
        /// 表情メニュー（FaceEmoと同じく、表情セットと固定の表情を「モード」として並べる）と、
        /// 選んだ表情セットのジェスチャーの割り当て。
        /// </summary>
        private void DrawMenuTab(ExpressionSet set)
        {
            var width = CenterWidth;

            _showSwitchEffect = EditorGUILayout.Foldout(_showSwitchEffect, "固定の表情の切り替え演出", true);
            if (_showSwitchEffect)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    DrawSwitchEffect(set, set.fixedSwitchEffect, "切り替え演出を変更");
                    EditorGUILayout.LabelField("固定の表情に切り替えるとき、間に別の表情（目閉じなど）を挟めます。表情ごとの設定は右の設定欄で変えられます。",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.LabelField("表情メニュー", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "ゲーム内の「表情選択」には、この並びのまま、表情セットのジェスチャーに割り当てた表情と固定だけの表情が自動で並び、選ぶと固定されます。" +
                "表情セットが2つ以上あるときは、ゲーム内のメニューで表情セットを切り替えられます（★が何も選んでいないときの表情セット）。" +
                "表情セットの切り替えと表情選択は、後から選んだほうが表示されます。" +
                "ここに追加するのは、ジェスチャーに割り当てない固定だけの表情です（ライブラリからドロップ。ドラッグで並べ替え・フォルダへ移動）。",
                EditorStyles.wordWrappedMiniLabel);

            var defaultNode = ExpressionSetUtility.DefaultMode(set);
            foreach (var (node, depth) in VisibleMenuRows(set))
            {
                DrawMenuRow(set, node, depth, node == defaultNode, width);
            }

            var endRect = GUILayoutUtility.GetRect(width, 30);
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(endRect, new Color(0, 0, 0, 0.12f));
            GUI.Label(endRect, "ここにドロップすると、メニューの一番下に追加", DropHint);
            HandleMenuDrop(set, endRect, null);

            using (new EditorGUILayout.HorizontalScope())
            {
                var selectedFolder = set.menu.Find(n => n.id == _selectedNodeId && n.kind == MenuNodeKind.Folder);
                var parentId = selectedFolder != null ? selectedFolder.id : "";
                if (GUILayout.Button("表情セットを追加"))
                {
                    var gestureSet = new GestureSet { name = $"セット{set.gestureSets.Count + 1}" };
                    var node = new MenuNode { kind = MenuNodeKind.GestureSet, gestureSetId = gestureSet.id };
                    Modify(set, "表情セットを追加", () =>
                    {
                        set.gestureSets.Add(gestureSet);
                        ExpressionSetUtility.MoveNode(set, node, parentId, null);
                    });
                    _selectedNodeId = node.id;
                }
                if (GUILayout.Button("フォルダを追加"))
                {
                    var node = new MenuNode { kind = MenuNodeKind.Folder, name = "新しいフォルダ" };
                    Modify(set, "フォルダを追加", () => ExpressionSetUtility.MoveNode(set, node, parentId, null));
                    _selectedNodeId = node.id;
                }
            }

            var missing = _libraryClips.Where(c => !InMenu(set, c)).ToList();
            using (new EditorGUI.DisabledScope(missing.Count == 0))
            {
                if (GUILayout.Button($"メニューに無いライブラリの表情を「その他」フォルダに追加（{missing.Count} 件）"))
                {
                    Modify(set, "その他に追加", () =>
                    {
                        var folder = set.menu.Find(n => n.kind == MenuNodeKind.Folder && string.IsNullOrEmpty(n.parentId) && n.name == "その他");
                        if (folder == null)
                        {
                            folder = new MenuNode { kind = MenuNodeKind.Folder, name = "その他" };
                            set.menu.Add(folder);
                        }
                        foreach (var clip in missing)
                        {
                            var expression = ExpressionSetUtility.FindOrCreateExpression(set, clip);
                            set.menu.Add(new MenuNode { kind = MenuNodeKind.Expression, expressionId = expression.id, parentId = folder.id });
                        }
                    });
                    MarkLibraryDirty();
                }
            }

            DrawGestureSetsOutsideMenu(set);

            EditorGUILayout.Space(12);
            var selected = set.menu.Find(n => n.id == _selectedNodeId);
            var selectedSet = selected != null && selected.kind == MenuNodeKind.GestureSet ? set.FindGestureSet(selected.gestureSetId) : null;
            if (selectedSet != null)
            {
                DrawGestureSetSection(set, selectedSet);
            }
            else
            {
                EditorGUILayout.HelpBox("メニューの表情セットを選ぶと、ここにジェスチャーの割り当てが出ます。", MessageType.None);
            }
        }

        private IEnumerable<(MenuNode node, int depth)> VisibleMenuRows(ExpressionSet set)
        {
            var hiddenUnder = new HashSet<string>();
            foreach (var (node, depth) in ExpressionSetUtility.TreeOrder(set))
            {
                if (!string.IsNullOrEmpty(node.parentId) && hiddenUnder.Contains(node.parentId))
                {
                    hiddenUnder.Add(node.id);
                    continue;
                }
                if (node.kind == MenuNodeKind.Folder && _collapsedFolders.Contains(node.id)) hiddenUnder.Add(node.id);
                yield return (node, depth);
            }
        }

        private void DrawMenuRow(ExpressionSet set, MenuNode node, int depth, bool isDefault, float width)
        {
            var rect = GUILayoutUtility.GetRect(width, MenuRowHeight);
            var selected = node.id == _selectedNodeId;
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1, rect.width, rect.height - 2),
                    selected ? new Color(0.24f, 0.48f, 0.9f, 0.4f) : new Color(0, 0, 0, 0.12f));
            }

            HandleMenuDrop(set, rect, node);

            var expression = node.kind == MenuNodeKind.Expression ? set.FindExpression(node.expressionId) : null;
            HandleMenuDrag(rect, node, expression != null ? expression.clip : null);

            var x = rect.x + 4 + depth * MenuIndent;
            var iconRect = new Rect(x + 14, rect.y + 2, MenuRowHeight - 4, MenuRowHeight - 4);

            switch (node.kind)
            {
                case MenuNodeKind.Folder:
                {
                    var collapsed = _collapsedFolders.Contains(node.id);
                    var open = EditorGUI.Foldout(new Rect(x, rect.y + 11, 14, 18), !collapsed, GUIContent.none);
                    if (open == collapsed)
                    {
                        if (open) _collapsedFolders.Remove(node.id);
                        else _collapsedFolders.Add(node.id);
                    }
                    GUI.DrawTexture(new Rect(iconRect.x + 6, iconRect.y + 6, 24, 24), EditorGUIUtility.IconContent("Folder Icon").image);
                    break;
                }
                case MenuNodeKind.GestureSet:
                    GUI.DrawTexture(new Rect(iconRect.x + 6, iconRect.y + 6, 24, 24), EditorGUIUtility.IconContent("AnimatorController Icon").image);
                    break;
                case MenuNodeKind.Expression:
                    var thumbnail = ExpressionThumbnail(expression);
                    if (thumbnail != null) GUI.DrawTexture(iconRect, thumbnail, ScaleMode.ScaleToFit);
                    break;
            }

            var buttonsWidth = 26 * 4;
            var textRect = new Rect(iconRect.xMax + 6, rect.y + 4, rect.xMax - iconRect.xMax - buttonsWidth - 12, 18);
            if (node.kind == MenuNodeKind.Folder && selected)
            {
                EditorGUI.BeginChangeCheck();
                var name = EditorGUI.TextField(textRect, node.name);
                if (EditorGUI.EndChangeCheck()) Modify(set, "フォルダ名を変更", () => node.name = name);
            }
            else
            {
                GUI.Label(textRect, ExpressionSetUtility.NodeName(set, node), EditorStyles.boldLabel);
            }
            string kindLabel;
            var duplicated = false;
            switch (node.kind)
            {
                case MenuNodeKind.Folder:
                    kindLabel = $"フォルダ（{ExpressionSetUtility.Children(set, node.id).Count()} 件）";
                    break;
                case MenuNodeKind.GestureSet:
                    var gestureSet = set.FindGestureSet(node.gestureSetId);
                    var count = gestureSet != null ? ExpressionSetUtility.GestureExpressions(set, gestureSet).Count : 0;
                    kindLabel = $"表情セット（表情選択に {count} 件）";
                    break;
                default:
                    duplicated = expression != null && ExpressionSetUtility.IsInGestureOfMenu(set, expression.id);
                    kindLabel = duplicated ? "固定だけ（ジェスチャーと重複：表情選択に重ねて並びます）" : "固定だけ";
                    break;
            }
            GUI.Label(new Rect(textRect.x, textRect.yMax, textRect.width, 16), kindLabel, duplicated ? WarningMiniLabel : EditorStyles.miniLabel);

            var bx = rect.xMax - buttonsWidth - 4;
            if (ExpressionSetUtility.IsMode(node))
            {
                var star = new GUIContent(isDefault ? "★" : "☆", "何も選んでいないときの状態にする");
                if (GUI.Button(new Rect(bx, rect.y + 10, 24, 20), star) && !isDefault)
                {
                    Modify(set, "既定のモードを変更", () => set.defaultModeId = node.id);
                }
            }
            if (GUI.Button(new Rect(bx + 26, rect.y + 10, 24, 20), "↑")) MoveSibling(set, node, -1);
            if (GUI.Button(new Rect(bx + 52, rect.y + 10, 24, 20), "↓")) MoveSibling(set, node, 1);
            if (GUI.Button(new Rect(bx + 78, rect.y + 10, 24, 20), new GUIContent("×", "メニューから外す")))
            {
                var message = node.kind == MenuNodeKind.Folder
                    ? $"フォルダ「{node.name}」と中の項目をメニューから外しますか？（表情や表情セットそのものは消えません）"
                    : $"「{ExpressionSetUtility.NodeName(set, node)}」をメニューから外しますか？（表情や表情セットそのものは消えません）";
                if (EditorUtility.DisplayDialog("メニューから外す", message, "外す", "キャンセル"))
                {
                    Modify(set, "メニューから外す", () => ExpressionSetUtility.RemoveNode(set, node));
                    MarkLibraryDirty();
                    GUIUtility.ExitGUI();
                }
            }

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _selectedNodeId = node.id;
                if (expression != null) Select(SelectionKind.Expression, expression.id, null);
                GUI.FocusControl(null);
                Repaint();
            }
        }

        /// <summary>
        /// メニューに置いていない表情セット（取り込んだが使っていないものなど）。
        /// </summary>
        private void DrawGestureSetsOutsideMenu(ExpressionSet set)
        {
            var outside = set.gestureSets
                .Where(s => !set.menu.Any(n => n.kind == MenuNodeKind.GestureSet && n.gestureSetId == s.id))
                .ToList();
            if (outside.Count == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("メニューに無い表情セット", EditorStyles.boldLabel);
            foreach (var gestureSet in outside)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(gestureSet.name);
                    if (GUILayout.Button("メニューに追加", GUILayout.Width(100)))
                    {
                        Modify(set, "表情セットをメニューに追加", () => ExpressionSetUtility.MoveNode(set,
                            new MenuNode { kind = MenuNodeKind.GestureSet, gestureSetId = gestureSet.id }, "", null));
                    }
                    if (GUILayout.Button("削除", GUILayout.Width(44)) &&
                        EditorUtility.DisplayDialog("表情セットを削除", $"表情セット「{gestureSet.name}」を削除しますか？", "削除", "キャンセル"))
                    {
                        Modify(set, "表情セットを削除", () => set.gestureSets.Remove(gestureSet));
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void MoveSibling(ExpressionSet set, MenuNode node, int delta)
        {
            var siblings = ExpressionSetUtility.Children(set, node.parentId).ToList();
            var index = siblings.IndexOf(node);
            var target = index + delta;
            if (target < 0 || target >= siblings.Count) return;

            // 下へ動かすときは、1つ下の項目のさらに次の前に入れる。
            var before = delta < 0 ? siblings[target] : target + 1 < siblings.Count ? siblings[target + 1] : null;
            Modify(set, "メニューを並べ替え", () => ExpressionSetUtility.MoveNode(set, node, node.parentId, before));
        }

        private void HandleMenuDrag(Rect rect, MenuNode node, AnimationClip clip)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _menuDragCandidate = node.id;
            }
            else if (e.type == EventType.MouseDrag && _menuDragCandidate == node.id && rect.Contains(e.mousePosition))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(MenuNodeDragKey, node.id);
                // 固定の表情は、ジェスチャーの枠にもそのままドロップできるようにクリップも渡す。
                DragAndDrop.objectReferences = clip != null ? new Object[] { clip } : new Object[0];
                DragAndDrop.StartDrag(node.name ?? clip?.name ?? "");
                _menuDragCandidate = null;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                _menuDragCandidate = null;
            }
        }

        /// <summary>
        /// メニューの項目、またはクリップのドロップを受け付ける。target がフォルダならその中に、
        /// それ以外ならその前に入れる。target が null ならメニューの一番下に入れる。
        /// </summary>
        private void HandleMenuDrop(ExpressionSet set, Rect rect, MenuNode target)
        {
            var e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!rect.Contains(e.mousePosition)) return;

            var draggedId = DragAndDrop.GetGenericData(MenuNodeDragKey) as string;
            var dragged = draggedId != null ? set.menu.Find(n => n.id == draggedId) : null;
            var clip = dragged == null ? DragAndDrop.objectReferences.OfType<AnimationClip>().FirstOrDefault() : null;
            if (dragged == null && clip == null) return;

            DragAndDrop.visualMode = dragged != null ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragUpdated)
            {
                e.Use();
                return;
            }

            DragAndDrop.AcceptDrag();
            e.Use();

            var parentId = target == null ? "" : target.kind == MenuNodeKind.Folder ? target.id : target.parentId;
            var before = target == null || target.kind == MenuNodeKind.Folder ? null : target;
            if (dragged != null)
            {
                Modify(set, "メニューの項目を移動", () => ExpressionSetUtility.MoveNode(set, dragged, parentId, before));
            }
            else
            {
                var node = new MenuNode { kind = MenuNodeKind.Expression };
                Modify(set, "メニューに表情を追加", () =>
                {
                    node.expressionId = ExpressionSetUtility.FindOrCreateExpression(set, clip).id;
                    ExpressionSetUtility.MoveNode(set, node, parentId, before);
                });
                _selectedNodeId = node.id;
                MarkLibraryDirty();

                // ジェスチャーに割り当て済みの表情は「表情選択」に自動で並ぶので、二重になることを知らせる（追加はする）。
                if (ExpressionSetUtility.IsInGestureOfMenu(set, node.expressionId))
                {
                    ShowNotification(new GUIContent($"「{clip.name}」はジェスチャーに割り当て済みで、表情選択に重ねて並びます（重複）"), 3);
                }
            }
            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// 切り替え演出の設定欄（有効・無効、挟む表情、挟む時間、戻る時間）。
        /// </summary>
        private void DrawSwitchEffect(ExpressionSet set, SwitchEffect effect, string undoName)
        {
            var ids = set.expressions.Select(e => e.id).ToList();
            var names = set.expressions.Select(e => e.name).ToArray();

            EditorGUI.BeginChangeCheck();
            var enabled = EditorGUILayout.ToggleLeft("間に表情を挟む", effect.enabled);
            var index = ids.IndexOf(effect.betweenExpressionId);
            int nextIndex;
            float hold, fade;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                nextIndex = EditorGUILayout.Popup("挟む表情", index, names);
                hold = EditorGUILayout.Slider("挟む時間（秒）", effect.holdTime, 0f, 1f);
                fade = EditorGUILayout.Slider("戻る時間（秒）", effect.fadeTime, 0f, 1f);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Modify(set, undoName, () =>
                {
                    effect.enabled = enabled;
                    if (nextIndex >= 0) effect.betweenExpressionId = ids[nextIndex];
                    effect.holdTime = hold;
                    effect.fadeTime = fade;
                });
            }

            if (enabled && index < 0)
            {
                EditorGUILayout.HelpBox("挟む表情を選んでください。選ぶまでは演出なしで切り替わります。", MessageType.Info);
            }
        }

        private static bool InMenu(ExpressionSet set, AnimationClip clip)
        {
            var expression = ExpressionSetUtility.FindExpressionByClip(set, clip);
            return expression != null &&
                   set.menu.Any(n => n.kind == MenuNodeKind.Expression && n.expressionId == expression.id);
        }
    }
}
