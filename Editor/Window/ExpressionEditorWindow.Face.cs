using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 「まばたき・口」タブ。まばたきと利用者が指定した口キャンセル、および
    /// 表情ごとのまばたき・視線・リップシンク・口モーフキャンセラーの一覧。
    /// </summary>
    internal partial class ExpressionEditorWindow
    {
        private const float FlagColumnWidth = 64f;

        [SerializeField] private bool _showBlinkSettings;
        [SerializeField] private bool _showMouthSettings;

        private List<BlinkShape> BlinkShapesInUse(ExpressionSet set) => set.blinkPose != null ? BlinkPoseUtility.Shapes(set.blinkPose, AvatarRoot, set) : set.customBlink ? set.blinkShapes : AvatarFaceDefaults.DescriptorBlinkShapes(Descriptor);
        private List<BlendShapeRef> MouthMorphsInUse(ExpressionSet set) => FaceControlPlan.ResolveMouthShapes(set, AvatarRoot)
            .Select(b => new BlendShapeRef { path = b.path, blendShape = b.propertyName.Substring(11) }).ToList();

        private void DrawFaceTab(ExpressionSet set)
        {
            var missingBlink = FaceControlPlan.BlinkSetupWarning(set, AvatarRoot) != null;
            if (missingBlink)
            {
                EditorGUILayout.LabelField("まばたきの設定（設定が必要です）", EditorStyles.boldLabel);
                DrawBlinkSettings(set);
                EditorGUILayout.Space(12);
            }
            DrawExpressionFlags(set);
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("共通の動作設定", EditorStyles.boldLabel);
            if (!missingBlink)
            {
                _showBlinkSettings = EditorGUILayout.Foldout(_showBlinkSettings, "まばたきの設定", true);
                if (_showBlinkSettings) DrawBlinkSettings(set);
            }
            _showMouthSettings = EditorGUILayout.Foldout(_showMouthSettings, "口キャンセルの設定", true);
            if (_showMouthSettings) DrawMouthCanceler(set);
        }

        private void DrawBlinkSettings(ExpressionSet set)
        {
            if (Event.current.type == EventType.Layout && set.blinkSource == BlinkSourceMode.Existing && set.blinkAnimation == null)
                Modify(set, "未指定の瞬き設定を更新", () => set.blinkSource = BlinkSourceMode.Generated);
            var current = set.UsesExistingBlink ? set.blinkAnimation : null;
            var animation = (AnimationClip)EditorGUILayout.ObjectField("アニメーションクリップ", current, typeof(AnimationClip), false);
            if (animation != current)
                Modify(set, "瞬きアニメーションを指定", () =>
                {
                    set.blinkAnimation = animation;
                    set.blinkSource = animation != null ? BlinkSourceMode.Existing : BlinkSourceMode.Generated;
                });
            if (animation != null)
            {
                EditorGUILayout.LabelField("指定したアニメーションをそのまま使います。", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("この瞬きをプレビュー")) Select(SelectionKind.Clip, null, animation);
                return;
            }
            if (set.blinkPose == null)
            {
                EditorGUILayout.LabelField("クリップ未指定。既存クリップを指定するか、新規作成してください。", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("専用の瞬きを新規作成"))
                {
                    BlinkPoseUtility.Editable(set, AvatarRoot);
                    Modify(set, "専用の瞬きを使用", () => set.blinkSource = BlinkSourceMode.Generated);
                    BlinkPoseEditorWindow.Open(_avatar);
                }
                return;
            }
            EditorGUILayout.LabelField("作成した目閉じから、ループする瞬きを自動生成します。", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("目閉じを編集・プレビュー")) BlinkPoseEditorWindow.Open(_avatar);
        }

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
        /// 指定クリップの口キャンセル用シェイプキー。選択した項目だけを記録する。
        /// </summary>
        private Vector2 _mouthRegisteredScroll;
        [SerializeField] private bool _showMouthShapeList = true;

        private void DrawRegisteredMouthShapes(ExpressionSet set)
        {
            var clip = set.mouthCancelAnimation;
            var bindings = clip != null ? AnimationUtility.GetCurveBindings(clip)
                .Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape.") &&
                    !LipSyncProtection.IsProtected(b, AvatarRoot, set)).ToArray() : new EditorCurveBinding[0];
            if (bindings.Length == 0)
                EditorGUILayout.LabelField("＋からシェイプキーを追加できます。", EditorStyles.centeredGreyMiniLabel);
            else
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(_mouthRegisteredScroll, GUILayout.Height(Mathf.Min(240, bindings.Length * 24 + 8))))
                {
                    _mouthRegisteredScroll = scroll.scrollPosition;
                    foreach (var binding in bindings)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var shape = binding.propertyName.Substring(11);
                        var hint = MouthCancelEditing.BaseFaceHighlight(Variant, binding.path, shape);
                        var labelRect = GUILayoutUtility.GetRect(new GUIContent(shape), EditorStyles.label, GUILayout.ExpandWidth(true));
                        if (hint != null && Event.current.type == EventType.Repaint) EditorGUI.DrawRect(labelRect, new Color(0.3f, 0.6f, 1f, 0.22f));
                        GUI.Label(labelRect, new GUIContent((hint != null ? "● " : "") + shape, hint ?? binding.path));
                        var value = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0);
                        EditorGUI.BeginChangeCheck();
                        var next = EditorGUILayout.FloatField(value, GUILayout.Width(65));
                        if (EditorGUI.EndChangeCheck())
                        {
                            clip = ExpressionSetUtility.MakeMouthCancelEditable(set);
                            MouthCancelEditing.SetValue(clip, binding, next);
                            AssetDatabase.SaveAssetIfDirty(clip);
                        }
                        if (GUILayout.Button("−", GUILayout.Width(25)))
                        {
                            RemoveRegisteredMouthShapes(set, new[] { binding });
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
            if (bindings.Length > 0 && GUILayout.Button("すべて削除")) RemoveRegisteredMouthShapes(set, bindings);
        }

        private static void RemoveRegisteredMouthShapes(ExpressionSet set, EditorCurveBinding[] bindings)
        {
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            var clip = ExpressionSetUtility.MakeMouthCancelEditable(set);
            if (clip == null) return;
            Undo.RecordObject(clip, "口キャンセルの項目を削除");
            foreach (var binding in bindings) AnimationUtility.SetEditorCurve(clip, binding, null);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            Undo.CollapseUndoOperations(group);
        }

        private void DrawMouthCanceler(ExpressionSet set)
        {
            EditorGUILayout.LabelField("既存の口キャンセルを置き換える場合は、「アバター設定」で元FXレイヤーをチェックしてください。", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("置き換える元FXレイヤーを選ぶ"))
            {
                _tab = Tab.Setup;
                _showReplacementLayers = true;
                _centerScroll = Vector2.zero;
            }
            var manual = (AnimationClip)EditorGUILayout.ObjectField(
                new GUIContent("クリップ", "指定クリップの0秒時点のシェイプキー値を使います。編集時は専用コピーを作成します。"),
                set.mouthCancelAnimation, typeof(AnimationClip), false);
            if (manual != set.mouthCancelAnimation)
            {
                Modify(set, "口キャンセルの指定", () => set.mouthCancelAnimation = manual);
                _showMouthShapeList = true;
            }
            if (set.mouthCancelAnimation == null)
            {
                EditorGUILayout.LabelField("未設定：ツール側の口キャンセルは無効です。クリップを指定するか、シェイプキーを選んで作成してください。", EditorStyles.wordWrappedMiniLabel);
                var createRect = GUILayoutUtility.GetRect(new GUIContent("シェイプキーを選んで新規作成"), GUI.skin.button);
                if (GUI.Button(createRect, "シェイプキーを選んで新規作成"))
                {
                    _showMouthShapeList = true;
                    MouthCancelSelectionWindow.Open(_avatar, createRect);
                    GUIUtility.ExitGUI();
                }
                return;
            }

            _showMouthShapeList = EditorGUILayout.Foldout(_showMouthShapeList, "口変形キャンセル用シェイプキー", true);
            if (!_showMouthShapeList) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("話している間、登録した値に戻します。＋で項目を追加できます。", EditorStyles.wordWrappedMiniLabel);
                DrawRegisteredMouthShapes(set);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    var addRect = GUILayoutUtility.GetRect(new GUIContent("＋"), GUI.skin.button, GUILayout.Width(28));
                    if (GUI.Button(addRect, new GUIContent("＋", "シェイプキーを複数選択して追加")))
                        MouthCancelSelectionWindow.Open(_avatar, addRect);
                }
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
