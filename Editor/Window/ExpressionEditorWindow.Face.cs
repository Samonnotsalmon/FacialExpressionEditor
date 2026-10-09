using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 「まばたき・口」タブ。まばたきと口モーフキャンセラー（標準は元アバターのもの。自分で編集もできる）と、
    /// 表情ごとのまばたき・視線・リップシンク・口モーフキャンセラーの一覧。
    /// </summary>
    internal partial class ExpressionEditorWindow
    {
        private const float FlagColumnWidth = 64f;

        [SerializeField] private bool _showBlinkShapes;
        [SerializeField] private bool _showBlinkSettings;
        [SerializeField] private bool _showMouthSettings;
        [SerializeField] private bool _showMouthMorphs;

        private List<BlinkShape> BlinkShapesInUse(ExpressionSet set) => set.customBlink ? set.blinkShapes : FaceDefaults.BlinkShapes;
        private List<BlendShapeRef> MouthMorphsInUse(ExpressionSet set) => set.mouthCancelAnimation != null
            ? AnimationUtility.GetCurveBindings(set.mouthCancelAnimation).Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape."))
                .Select(b => new BlendShapeRef { path = b.path, blendShape = b.propertyName.Substring(11) }).ToList()
            : set.customMouthMorphs ? set.mouthMorphs : FaceDefaults.MouthMorphs;

        private void DrawFaceTab(ExpressionSet set)
        {
            DrawExpressionFlags(set);
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("共通の動作設定", EditorStyles.boldLabel);
            var missingBlink = set.blinkAnimation == null && BlinkShapesInUse(set).Count == 0 && (set.customBlink || FaceDefaults.BlinkClip == null);
            _showBlinkSettings = EditorGUILayout.Foldout(_showBlinkSettings || missingBlink,
                missingBlink ? "まばたきの設定（対象を指定してください）" : "まばたきの設定", true);
            if (_showBlinkSettings) DrawBlinkSettings(set);
            _showMouthSettings = EditorGUILayout.Foldout(_showMouthSettings, "口キャンセルの設定", true);
            if (_showMouthSettings) DrawMouthCanceler(set);
        }

        private void DrawBlinkSettings(ExpressionSet set)
        {
            EditorGUILayout.LabelField("まばたき", EditorStyles.boldLabel);
            var manual = (AnimationClip)EditorGUILayout.ObjectField("指定クリップ（D&D）", set.blinkAnimation, typeof(AnimationClip), false);
            if (manual != set.blinkAnimation) Modify(set, "まばたきクリップを指定", () => set.blinkAnimation = manual);
            if (manual != null)
            {
                EditorGUILayout.HelpBox("指定クリップを繰り返し再生します。下の自動検出・シェイプキー設定より優先します。", MessageType.Info);
                if (GUILayout.Button("この瞬きをプレビュー")) Select(SelectionKind.Clip, null, manual);
                return;
            }
            EditorGUILayout.LabelField("ビルド時にVRChatのまばたきを止め、下のまばたきのアニメーションに置き換えます（まばたきを止める表情でも、視線は動かせます）。",
                EditorStyles.wordWrappedMiniLabel);

            var defaults = FaceDefaults;
            EditorGUILayout.LabelField("使うまばたき", manual != null ? manual.name : set.customBlink ? "シェイプキーから生成" : defaults.BlinkSource);
            if (defaults.BlinkLayer != null)
            {
                EditorGUILayout.LabelField($"元FXの「{defaults.BlinkLayer}」レイヤーは、生成したまばたきに置き換えます。", EditorStyles.wordWrappedMiniLabel);
            }

            if (manual == null && !set.customBlink && defaults.BlinkClip != null)
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("自動検出したクリップ", defaults.BlinkClip, typeof(AnimationClip), false);
            }
            EditorGUILayout.LabelField("クリップがない場合はシェイプキーから瞬きを生成して置き換えます。対象も見つからない場合は指定が必要です。", EditorStyles.wordWrappedMiniLabel);
            DrawCustomToggle(set, set.customBlink, "まばたきを編集", on =>
            {
                set.customBlink = on;
                if (on && set.blinkShapes.Count == 0)
                {
                    set.blinkShapes = defaults.BlinkShapes
                        .Select(s => new BlinkShape { path = s.path, blendShape = s.blendShape, closedValue = s.closedValue })
                        .ToList();
                }
            });
            if (set.customBlink && defaults.BlinkClip != null)
            {
                EditorGUILayout.LabelField("編集すると、元FXのアニメーションではなく、シェイプキーごとに作ったまばたきのアニメーションを使います。",
                    EditorStyles.wordWrappedMiniLabel);
            }

            var shapes = BlinkShapesInUse(set);
            if (manual == null && shapes.Count == 0)
            {
                EditorGUILayout.HelpBox(set.customBlink
                    ? "まばたきで動かすシェイプキーがありません。下の「シェイプキーを選ぶ…」で追加してください。"
                    : "元アバターにまばたきが見つかりません。「自分で編集する」をオンにして、シェイプキーを追加してください。", MessageType.Error);
            }

            _showBlinkShapes = EditorGUILayout.Foldout(_showBlinkShapes, $"動かすシェイプキー（{shapes.Count} 件）", true);
            if (_showBlinkShapes)
            {
                foreach (var shape in shapes.ToList())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(new GUIContent(shape.blendShape, shape.path));
                        using (new EditorGUI.DisabledScope(!set.customBlink))
                        {
                            EditorGUI.BeginChangeCheck();
                            var closed = EditorGUILayout.FloatField(new GUIContent("閉じたとき", "目を閉じたときの値"), shape.closedValue, GUILayout.Width(150));
                            if (EditorGUI.EndChangeCheck()) Modify(set, "まばたきを変更", () => shape.closedValue = closed);
                        }
                        if (set.customBlink && GUILayout.Button(new GUIContent("×", "外す"), EditorStyles.miniButton, GUILayout.Width(24)))
                        {
                            Modify(set, "まばたきのシェイプキーを外す", () => set.blinkShapes.Remove(shape));
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }

            if (set.customBlink)
            {
                DrawShapePickerButton((path, name) => set.blinkShapes.Any(s => s.path == path && s.blendShape == name),
                    (path, name, on) => Modify(set, on ? "まばたきのシェイプキーを追加" : "まばたきのシェイプキーを外す", () =>
                    {
                        if (on) set.blinkShapes.Add(new BlinkShape { path = path, blendShape = name });
                        else set.blinkShapes.RemoveAll(s => s.path == path && s.blendShape == name);
                    }));
            }
        }

        /// <summary>
        /// 表情ごとのまばたき・視線・リップシンク・口モーフキャンセラーを一覧で切り替える。
        /// </summary>
        private void DrawExpressionFlags(ExpressionSet set)
        {
            EditorGUILayout.LabelField("表情ごとの設定", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("チェックを外した表情の間は、その動きを止めます。まばたきとリップシンクは、元FXの設定を取り込んでいます。" +
                                       "目を閉じる表情は、視線も止めると自然になります。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.LabelField("ジェスチャーまたは固定メニューに割り当てた表情のみ表示します。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("チェックを押したまま上下にドラッグすると、同じ列をまとめてON／OFFにできます。", EditorStyles.wordWrappedMiniLabel);
            var dragControl = GUIUtility.GetControlID("ExpressionFlags".GetHashCode(), FocusType.Passive);
            var flagEvent = Event.current;
            if (_flagDragColumn >= 0 && (GUIUtility.hotControl != dragControl || _flagDragSet != set)) EndFlagDrag();

            var hasCanceler = MouthMorphsInUse(set).Count > 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("表情", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                foreach (var label in new[] { "まばたき", "視線", "リップシンク", "口キャンセル" })
                {
                    GUILayout.Label(label, EditorStyles.miniBoldLabel, GUILayout.Width(FlagColumnWidth));
                }
            }

            var assigned = ExpressionSetUtility.AssignedExpressions(set);
            if (assigned.Count == 0) EditorGUILayout.HelpBox("ジェスチャーまたは固定メニューに表情を割り当てると、ここに表示されます。", MessageType.Info);
            foreach (var expression in assigned)
            {
                var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(30));
                if (IsSelected(SelectionKind.Expression, expression.id) && Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.3f));
                }

                var thumbnail = ExpressionThumbnail(expression);
                var thumbRect = GUILayoutUtility.GetRect(28, 28, GUILayout.Width(28), GUILayout.Height(28));
                if (thumbnail != null) GUI.DrawTexture(thumbRect, thumbnail, ScaleMode.ScaleToFit);
                if (GUILayout.Button(expression.name, EditorStyles.label, GUILayout.Height(28)))
                {
                    Select(SelectionKind.Expression, expression.id, null);
                }
                GUILayout.FlexibleSpace();

                DrawFlagToggle(set, expression, 0, dragControl);
                DrawFlagToggle(set, expression, 1, dragControl);
                DrawFlagToggle(set, expression, 2, dragControl);
                using (new EditorGUI.DisabledScope(!expression.enableLipSync || !hasCanceler))
                    DrawFlagToggle(set, expression, 3, dragControl);
                EditorGUILayout.EndHorizontal();
            }
            if (_flagDragColumn >= 0 && flagEvent.type == EventType.MouseDrag)
            {
                _flagDragPosition = flagEvent.mousePosition;
                flagEvent.Use();
                Repaint();
            }
        }

        private int _flagDragColumn = -1;
        private int _flagDragControl;
        private int _flagDragUndoGroup;
        private bool _flagDragValue;
        private Vector2 _flagDragPosition;
        private ExpressionSet _flagDragSet;

        private void EndFlagDrag()
        {
            if (_flagDragColumn < 0) return;
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(_flagDragUndoGroup);
            if (GUIUtility.hotControl == _flagDragControl) GUIUtility.hotControl = 0;
            _flagDragColumn = -1;
            _flagDragSet = null;
        }

        private void OnLostFocus() => EndFlagDrag();

        private static bool GetExpressionFlag(Expression expression, int column)
        {
            switch (column)
            {
                case 0: return expression.enableBlink;
                case 1: return expression.enableEyeTracking;
                case 2: return expression.enableLipSync;
                default: return expression.mouthMorphCancel;
            }
        }

        private void SetExpressionFlag(ExpressionSet set, Expression expression, int column, bool value)
        {
            if (GetExpressionFlag(expression, column) == value) return;
            Modify(set, "表情の間の動きを変更", () =>
            {
                switch (column)
                {
                    case 0: expression.enableBlink = value; break;
                    case 1: expression.enableEyeTracking = value; break;
                    case 2: expression.enableLipSync = value; break;
                    case 3: expression.mouthMorphCancel = value; break;
                }
            });
        }

        private static bool FlagDragCrossesCell(Rect cell, Vector2 from, Vector2 to) =>
            from.x >= cell.xMin && from.x < cell.xMax && to.x >= cell.xMin && to.x < cell.xMax &&
            Mathf.Max(from.y, to.y) >= cell.yMin && Mathf.Min(from.y, to.y) < cell.yMax;

        private void DrawFlagToggle(ExpressionSet set, Expression expression, int column, int dragControl)
        {
            var cell = GUILayoutUtility.GetRect(FlagColumnWidth, 28, GUILayout.Width(FlagColumnWidth), GUILayout.Height(28));
            var toggle = new Rect(cell.x + 20, cell.y, 20, cell.height);
            var evt = Event.current;
            var value = GetExpressionFlag(expression, column);
            if (GUI.enabled && evt.type == EventType.MouseDown && evt.button == 0 && toggle.Contains(evt.mousePosition))
            {
                Undo.IncrementCurrentGroup();
                _flagDragUndoGroup = Undo.GetCurrentGroup();
                _flagDragColumn = column;
                _flagDragControl = dragControl;
                _flagDragSet = set;
                _flagDragValue = !value;
                _flagDragPosition = evt.mousePosition;
                GUIUtility.hotControl = dragControl;
                SetExpressionFlag(set, expression, column, _flagDragValue);
                evt.Use();
                Repaint();
            }
            // Fill skipped rows on fast vertical drags. Other columns and disabled cells stay unchanged.
            if (GUI.enabled && evt.type == EventType.MouseDrag && _flagDragColumn == column &&
                FlagDragCrossesCell(cell, _flagDragPosition, evt.mousePosition))
                SetExpressionFlag(set, expression, column, _flagDragValue);

            value = GetExpressionFlag(expression, column);
            var next = EditorGUI.Toggle(toggle, value);
            if (next != value) SetExpressionFlag(set, expression, column, next);
        }

        /// <summary>
        /// 口モーフキャンセラーのシェイプキー。標準は元FXの口モーフキャンセラーのもの。自分で編集すると追加・削除できる。
        /// </summary>
        private void DrawMouthCanceler(ExpressionSet set)
        {
            EditorGUILayout.LabelField("口モーフキャンセラー", EditorStyles.boldLabel);
            var manual = (AnimationClip)EditorGUILayout.ObjectField("対象クリップ（D&D）", set.mouthCancelAnimation, typeof(AnimationClip), false);
            var useValues = manual != null ? EditorGUILayout.ToggleLeft("クリップの先頭の値に戻す（オフならベース顔）", set.mouthCancelUseClipValues) : set.mouthCancelUseClipValues;
            if (manual != set.mouthCancelAnimation || useValues != set.mouthCancelUseClipValues)
                Modify(set, "口キャンセルの指定", () => { set.mouthCancelAnimation = manual; set.mouthCancelUseClipValues = useValues; });
            EditorGUILayout.LabelField("話している間、口のシェイプキーをベース顔の値に戻し、表情の口とリップシンクの口が重ならないようにします。" +
                                       "表情ごとに止められます（上の一覧）。",
                EditorStyles.wordWrappedMiniLabel);

            if (manual != null)
            {
                EditorGUILayout.LabelField($"指定クリップから {MouthMorphsInUse(set).Count} 個のシェイプキーを使用", EditorStyles.miniLabel);
                return;
            }

            var defaults = FaceDefaults;
            EditorGUILayout.LabelField("使うシェイプキー", set.customMouthMorphs ? "自分で編集したもの" : defaults.MouthMorphSource);
            if (defaults.MouthCancelerLayers.Count > 0)
            {
                EditorGUILayout.LabelField($"元FXの「{string.Join("」「", defaults.MouthCancelerLayers)}」レイヤーは、生成した口モーフキャンセラーに置き換えます。",
                    EditorStyles.wordWrappedMiniLabel);
            }

            DrawCustomToggle(set, set.customMouthMorphs, "口モーフキャンセラーを編集", on =>
            {
                set.customMouthMorphs = on;
                if (on && set.mouthMorphs.Count == 0)
                {
                    set.mouthMorphs = defaults.MouthMorphs.Select(m => new BlendShapeRef { path = m.path, blendShape = m.blendShape }).ToList();
                }
            });

            var morphs = MouthMorphsInUse(set);
            if (morphs.Count == 0)
            {
                EditorGUILayout.LabelField(set.customMouthMorphs
                    ? "シェイプキーが無いので、口モーフキャンセラーは作りません。"
                    : "元アバターに口モーフキャンセラーが無いので作りません。使うときは「自分で編集する」をオンにして追加してください。",
                    EditorStyles.wordWrappedMiniLabel);
            }

            _showMouthMorphs = EditorGUILayout.Foldout(_showMouthMorphs, $"戻すシェイプキー（{morphs.Count} 件）", true);
            if (_showMouthMorphs)
            {
                foreach (var morph in morphs.ToList())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(new GUIContent(morph.blendShape, morph.path));
                        if (set.customMouthMorphs && GUILayout.Button(new GUIContent("×", "外す"), EditorStyles.miniButton, GUILayout.Width(24)))
                        {
                            Modify(set, "口モーフキャンセラーのシェイプキーを外す", () => set.mouthMorphs.Remove(morph));
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }

            if (set.customMouthMorphs)
            {
                DrawShapePickerButton((path, name) => set.mouthMorphs.Any(m => m.path == path && m.blendShape == name),
                    (path, name, on) => Modify(set, on ? "口モーフキャンセラーのシェイプキーを追加" : "口モーフキャンセラーのシェイプキーを外す", () =>
                    {
                        if (on) set.mouthMorphs.Add(new BlendShapeRef { path = path, blendShape = name });
                        else set.mouthMorphs.RemoveAll(m => m.path == path && m.blendShape == name);
                    }));
            }
        }

        /// <summary>
        /// 「自分で編集する」のオン・オフ。オフにすると元アバターのものに戻る（編集した内容は残しておき、またオンにすると使う）。
        /// </summary>
        private void DrawCustomToggle(ExpressionSet set, bool custom, string undoName, Action<bool> apply)
        {
            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.ToggleLeft("自分で編集する（オフなら元アバターのものを使う）", custom);
            if (EditorGUI.EndChangeCheck())
            {
                Modify(set, undoName, () => apply(next));
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>
        /// シェイプキーを一覧からチェックで選ぶドロップダウンを開くボタン。チェックを付けると加え、外すと除く。
        /// </summary>
        private void DrawShapePickerButton(Func<string, string, bool> isOn, Action<string, string, bool> set)
        {
            var content = new GUIContent("シェイプキーを選ぶ…", "一覧からチェックで選びます（複数選べます）");
            var rect = GUILayoutUtility.GetRect(content, GUI.skin.button, GUILayout.Width(160));
            if (GUI.Button(rect, content))
            {
                PopupWindow.Show(rect, new BlendShapePicker(AvatarRoot, isOn, (path, name, on) =>
                {
                    set(path, name, on);
                    Repaint();
                }));
            }
        }
    }
}
