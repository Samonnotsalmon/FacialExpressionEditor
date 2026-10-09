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
            if (EditorGUI.EndChangeCheck())
            {
                Modify(set, "表情セットを変更", () =>
                {
                    gestureSet.name = name;
                    mapping.dominantHand = dominant;
                });
            }
            EditorGUILayout.LabelField("Fistの「握り具合」をオンにした手は、目元をベース顔からFistの表情へ動かします。口元は登録した完成表情のままです。" +
                                       "オフの手は、握るとそのまま表情が出ます。",
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
                var fistMapping = i == (int)HandGesture.Fist ? mapping : null;
                DrawSlot(new Rect(row.x + labelWidth, row.y, slotWidth, SlotHeight), set,
                    mapping.left[index], id => mapping.left[index] = id, $"{gestureSet.name} 左手 {GestureLabels[index]}",
                    Hand.Left, fistMapping);
                DrawSlot(new Rect(row.x + labelWidth + slotWidth + 4, row.y, slotWidth, SlotHeight), set,
                    mapping.right[index], id => mapping.right[index] = id, $"{gestureSet.name} 右手 {GestureLabels[index]}",
                    Hand.Right, fistMapping);
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

                // 優先する手がFistで握り具合をオンにしていれば、その手の握り具合で動く（ビルドと同じ）。
                var dominant = mapping.dominantHand;
                var dominantGesture = dominant == Hand.Left ? combo.left : combo.right;
                var gripHand = dominantGesture == HandGesture.Fist && mapping.UsesFistWeight(dominant) ? dominant : (Hand?)null;

                var slotX = row.x + popupWidth * 2 + 24;
                DrawSlot(new Rect(slotX, row.y, 200, SlotHeight), set, combo.expressionId,
                    id => combo.expressionId = id, "組み合わせ", gripHand ?? Hand.Left, null, gripHand != null);

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
        /// 表情を1つ割り当てる枠。クリップをドロップすると割り当て、クリックで選択、×で外す。＋で新しい表情を作って割り当てる。
        /// fistMapping があればFistのマスで、下に「握り具合」のオン・オフを出す（hand の手の設定）。
        /// 握り具合がオンのマス（組み合わせでは grip）を選ぶと、右のプレビューで握り具合を確認できる。
        /// </summary>
        private void DrawSlot(Rect rect, ExpressionSet set, string expressionId, Action<string> assign, string undoName,
            Hand hand = Hand.Left, GestureMapping fistMapping = null, bool grip = false)
        {
            var expression = set.FindExpression(expressionId);
            var thumbRect = new Rect(rect.x, rect.y, rect.height, rect.height);
            if (fistMapping != null) grip = fistMapping.UsesFistWeight(hand);

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
                GUI.Label(fistMapping != null ? new Rect(rect.x, rect.y, rect.width, rect.height - 20) : rect, "ここにドロップ", DropHint);
                DrawNewExpressionButton(new Rect(rect.xMax - 24, rect.y + 4, 22, 18), set, null, assign, undoName);
                if (fistMapping != null) DrawGripToggle(new Rect(rect.x + 6, rect.yMax - 20, rect.width - 12, 18), set, fistMapping, hand);
                return;
            }

            HandleDragSource(rect, expression.clip);
            var selected = grip
                ? IsSelected(SelectionKind.Fist, expression.id, null, hand)
                : IsSelected(SelectionKind.Expression, expression.id);
            if (DrawCell(thumbRect, ExpressionThumbnail(expression), "", selected))
            {
                if (grip) Select(SelectionKind.Fist, expression.id, null, hand);
                else Select(SelectionKind.Expression, expression.id, null);
            }

            var textRect = new Rect(thumbRect.xMax + 6, rect.y + 4, rect.width - thumbRect.width - 58, 18);
            GUI.Label(textRect, expression.name, EditorStyles.boldLabel);
            var noteY = textRect.yMax;
            var overrideEntry = Variant != null ? Variant.FindOverride(expression.id) : null;
            if (overrideEntry != null)
            {
                var label = FaceVariantUtility.IsEdited(overrideEntry, expression) ? "このバリアントで差し替え中" : "差し替え（未編集）";
                GUI.Label(new Rect(textRect.x, noteY, textRect.width, 16), label, EditorStyles.miniLabel);
                noteY += 16;
            }
            if (_usage.IsDuplicated(expression.clip))
            {
                var places = string.Join("、", _usage.Of(expression.clip).Select(u => u.Label));
                GUI.Label(new Rect(textRect.x, noteY, textRect.width, 16),
                    new GUIContent($"重複（{_usage.Of(expression.clip).Count} か所）", places), WarningMiniLabel);
            }

            DrawNewExpressionButton(new Rect(rect.xMax - 48, rect.y + 4, 22, 18), set, expression, assign, undoName);
            if (GUI.Button(new Rect(rect.xMax - 24, rect.y + 4, 22, 18), "×"))
            {
                Modify(set, undoName + "を外す", () => assign(null));
                GUIUtility.ExitGUI();
            }

            if (fistMapping != null)
            {
                DrawGripToggle(new Rect(thumbRect.xMax + 6, rect.yMax - 20, rect.width - thumbRect.width - 8, 18), set, fistMapping, hand);
            }
        }

        /// <summary>
        /// 新しい表情を作って、このマスに割り当てるボタン（今の顔から・空から・今の表情をコピー）。
        /// </summary>
        private void DrawNewExpressionButton(Rect rect, ExpressionSet set, Expression current, Action<string> assign, string place)
        {
            if (GUI.Button(rect, new GUIContent("＋", "新しい表情を作って、ここに割り当てます")))
            {
                ShowNewExpressionMenu(set, current, assign, place);
            }
        }

        /// <summary>
        /// Fistのマスの「握り具合」のオン・オフ（表情セットの、その手の設定）。
        /// </summary>
        private void DrawGripToggle(Rect rect, ExpressionSet set, GestureMapping mapping, Hand hand)
        {
            var on = mapping.UsesFistWeight(hand);
            var next = GUI.Toggle(rect, on, new GUIContent("握り具合（0でベース顔）",
                "オンにすると、握り具合0でベース顔（無表情）、握り切るとこのマスの表情になります。オフなら、握るとそのまま表情が出ます。"));
            if (next == on) return;

            Modify(set, "Fistの握り具合を変更", () =>
            {
                if (hand == Hand.Left) mapping.useLeftFistWeight = next;
                else mapping.useRightFistWeight = next;
            });
            if (!next && IsSelected(SelectionKind.Fist, _selectedId, null, hand)) Select(SelectionKind.Expression, _selectedId, null);
        }
    }
}
