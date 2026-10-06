using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private static readonly string[] HandLabels = { "左手", "右手" };
        private static string[] GestureLabels => ClipUsage.GestureShortLabels;

        private const float SlotHeight = 84f;

        [SerializeField] private bool _showNeutralGesture;

        /// <summary>
        /// 表情メニューで選んだ表情セットの、ジェスチャーの割り当て。
        /// </summary>
        private void DrawGestureSetSection(ExpressionSet set, GestureSet gestureSet)
        {
            var mapping = gestureSet.mapping;
            mapping.EnsureSize();

            EditorGUILayout.LabelField($"表情セット「{gestureSet.name}」のジェスチャー", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var name = EditorGUILayout.TextField("名前", gestureSet.name);
            var dominant = (Hand)EditorGUILayout.Popup("優先する手", (int)mapping.dominantHand, HandLabels);
            bool useLeft, useRight;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Fistの握り具合");
                useLeft = EditorGUILayout.ToggleLeft("左手で使う", mapping.useLeftFistWeight, GUILayout.Width(100));
                useRight = EditorGUILayout.ToggleLeft("右手で使う", mapping.useRightFistWeight, GUILayout.Width(100));
            }
            if (EditorGUI.EndChangeCheck())
            {
                Modify(set, "表情セットを変更", () =>
                {
                    gestureSet.name = name;
                    mapping.dominantHand = dominant;
                    mapping.useLeftFistWeight = useLeft;
                    mapping.useRightFistWeight = useRight;
                });
            }
            EditorGUILayout.LabelField("握り具合を使う手は、Fistの表情が握り具合に合わせて動きます（目閉じなど）。使わない手は、握るとそのまま表情が出ます。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            var width = CenterWidth;
            const float labelWidth = 96f;
            var slotWidth = Mathf.Min(220f, (width - labelWidth - 8) / 2);

            var header = GUILayoutUtility.GetRect(width, 18);
            GUI.Label(new Rect(header.x + labelWidth, header.y, slotWidth, 18), "左手", EditorStyles.boldLabel);
            GUI.Label(new Rect(header.x + labelWidth + slotWidth + 4, header.y, slotWidth, 18), "右手", EditorStyles.boldLabel);

            // Neutral（手が何もしていないとき）は無表情（ベース顔）になるので、ふだんは表に出さない。
            // 動く通常表情を使うときなどだけ、表示して割り当てる。
            var neutralAssigned = !string.IsNullOrEmpty(mapping.left[0]) || !string.IsNullOrEmpty(mapping.right[0]);
            using (new EditorGUI.DisabledScope(neutralAssigned))
            {
                _showNeutralGesture = EditorGUILayout.ToggleLeft(
                    "Neutral も設定する（ふだんは無表情。動く通常表情を使う場合など）", _showNeutralGesture || neutralAssigned);
            }

            for (var i = 0; i < GestureMapping.GestureCount; i++)
            {
                if (i == 0 && !_showNeutralGesture && !neutralAssigned) continue;

                var row = GUILayoutUtility.GetRect(width, SlotHeight + 4);
                GUI.Label(new Rect(row.x, row.y + SlotHeight / 2 - 9, labelWidth - 4, 18), GestureLabels[i]);

                var index = i;
                DrawSlot(new Rect(row.x + labelWidth, row.y, slotWidth, SlotHeight), set,
                    mapping.left[index], id => mapping.left[index] = id, $"{gestureSet.name} 左手 {GestureLabels[index]}");
                DrawSlot(new Rect(row.x + labelWidth + slotWidth + 4, row.y, slotWidth, SlotHeight), set,
                    mapping.right[index], id => mapping.right[index] = id, $"{gestureSet.name} 右手 {GestureLabels[index]}");
            }

            EditorGUILayout.Space();
            DrawCombos(set, gestureSet, width);
        }

        private void DrawCombos(ExpressionSet set, GestureSet gestureSet, float width)
        {
            var mapping = gestureSet.mapping;
            EditorGUILayout.LabelField("組み合わせの上書き", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("左右の表より優先されます（両手 Victory だけ別の表情にする、など）。", EditorStyles.miniLabel);

            const float popupWidth = 110f;
            foreach (var combo in mapping.combos.ToList())
            {
                var row = GUILayoutUtility.GetRect(width, SlotHeight + 4);
                EditorGUI.BeginChangeCheck();
                var left = (HandGesture)EditorGUI.Popup(new Rect(row.x, row.y + 30, popupWidth, 18), (int)combo.left, GestureLabels);
                GUI.Label(new Rect(row.x + popupWidth + 2, row.y + 30, 14, 18), "×");
                var right = (HandGesture)EditorGUI.Popup(new Rect(row.x + popupWidth + 18, row.y + 30, popupWidth, 18), (int)combo.right, GestureLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    Modify(set, "組み合わせを変更", () => { combo.left = left; combo.right = right; });
                }

                var slotX = row.x + popupWidth * 2 + 24;
                DrawSlot(new Rect(slotX, row.y, 200, SlotHeight), set, combo.expressionId,
                    id => combo.expressionId = id, "組み合わせ");

                if (GUI.Button(new Rect(slotX + 206, row.y + 30, 44, 18), "削除"))
                {
                    Modify(set, "組み合わせを削除", () => mapping.combos.Remove(combo));
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button("組み合わせを追加", GUILayout.Width(140)))
            {
                Modify(set, "組み合わせを追加", () => mapping.combos.Add(new GestureComboOverride
                {
                    left = HandGesture.Victory,
                    right = HandGesture.Victory,
                }));
            }
        }

        /// <summary>
        /// 表情を1つ割り当てる枠。クリップをドロップすると割り当て、クリックで選択、×で外す。
        /// </summary>
        private void DrawSlot(Rect rect, ExpressionSet set, string expressionId, Action<string> assign, string undoName)
        {
            var expression = set.FindExpression(expressionId);

            var dropped = AcceptClipDrop(rect);
            if (dropped != null)
            {
                Modify(set, undoName + "に割り当て", () => assign(ExpressionSetUtility.FindOrCreateExpression(set, dropped).id));
                MarkLibraryDirty();
                return;
            }

            if (expression == null)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.12f));
                }
                GUI.Label(rect, "ここにドロップ", DropHint);
                return;
            }

            var thumbRect = new Rect(rect.x, rect.y, rect.height, rect.height);
            HandleDragSource(rect, expression.clip);
            if (DrawCell(thumbRect, ExpressionThumbnail(expression), "", IsSelected(SelectionKind.Expression, expression.id)))
            {
                Select(SelectionKind.Expression, expression.id, null);
            }

            var textRect = new Rect(thumbRect.xMax + 6, rect.y + 4, rect.width - thumbRect.width - 34, 18);
            GUI.Label(textRect, expression.name, EditorStyles.boldLabel);
            var noteY = textRect.yMax;
            if (Variant != null && Variant.FindOverride(expression.id) != null)
            {
                GUI.Label(new Rect(textRect.x, noteY, textRect.width, 16), "このバリアントで差し替え中", EditorStyles.miniLabel);
                noteY += 16;
            }
            if (_usage.IsDuplicated(expression.clip))
            {
                var places = string.Join("、", _usage.Of(expression.clip).Select(u => u.Label));
                GUI.Label(new Rect(textRect.x, noteY, textRect.width, 16),
                    new GUIContent($"重複（{_usage.Of(expression.clip).Count} か所）", places), WarningMiniLabel);
            }

            if (GUI.Button(new Rect(rect.xMax - 24, rect.y + 4, 22, 18), "×"))
            {
                Modify(set, undoName + "を外す", () => assign(null));
                GUIUtility.ExitGUI();
            }
        }
    }
}
