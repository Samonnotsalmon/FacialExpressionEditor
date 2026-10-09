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

        [SerializeField] private bool _showBlinkShapes = true;
        [SerializeField] private bool _showMouthMorphs;

        private List<BlinkShape> BlinkShapesInUse(ExpressionSet set) => set.customBlink ? set.blinkShapes : FaceDefaults.BlinkShapes;
        private List<BlendShapeRef> MouthMorphsInUse(ExpressionSet set) => set.mouthCancelAnimation != null
            ? AnimationUtility.GetCurveBindings(set.mouthCancelAnimation).Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape."))
                .Select(b => new BlendShapeRef { path = b.path, blendShape = b.propertyName.Substring(11) }).ToList()
            : set.customMouthMorphs ? set.mouthMorphs : FaceDefaults.MouthMorphs;

        private void DrawFaceTab(ExpressionSet set)
        {
            DrawBlinkSettings(set);
            EditorGUILayout.Space(12);
            DrawExpressionFlags(set);
            EditorGUILayout.Space(12);
            DrawMouthCanceler(set);
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
            }
            EditorGUILayout.LabelField("ビルド時にVRChatのまばたきを止め、下のまばたきのアニメーションに置き換えます（まばたきを止める表情でも、視線は動かせます）。",
                EditorStyles.wordWrappedMiniLabel);

            var defaults = FaceDefaults;
            EditorGUILayout.LabelField("使うまばたき", set.customBlink ? "自分で編集したもの" : defaults.BlinkSource);
            if (defaults.BlinkLayer != null)
            {
                EditorGUILayout.LabelField($"元FXの「{defaults.BlinkLayer}」レイヤーは、生成したまばたきに置き換えます。", EditorStyles.wordWrappedMiniLabel);
            }

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
            if (shapes.Count == 0)
            {
                EditorGUILayout.HelpBox(set.customBlink
                    ? "まばたきで動かすシェイプキーがありません。下の「シェイプキーを選ぶ…」で追加してください。"
                    : "元アバターにまばたきが見つかりません。「自分で編集する」をオンにして、シェイプキーを追加してください。", MessageType.Warning);
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

            var descriptor = Descriptor;
            if (set.originalGestureLayers.Count > 0 && FxImporter.GetFx(descriptor) != null &&
                GUILayout.Button("まばたき・リップシンクを元FXの設定に戻す"))
            {
                var changed = 0;
                Modify(set, "まばたき・リップシンクを元FXの設定に戻す", () => changed = FxImporter.ImportFaceControl(descriptor, set));
                ShowNotification(new GUIContent($"{changed} 件の表情の設定を変えました"), 2);
            }

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

            foreach (var expression in set.expressions)
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

                EditorGUI.BeginChangeCheck();
                var blink = FlagToggle(expression.enableBlink);
                var eyes = FlagToggle(expression.enableEyeTracking);
                var lipSync = FlagToggle(expression.enableLipSync);
                bool cancel;
                using (new EditorGUI.DisabledScope(!lipSync || !hasCanceler))
                {
                    cancel = FlagToggle(expression.mouthMorphCancel);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Modify(set, "表情の間の動きを変更", () =>
                    {
                        expression.enableBlink = blink;
                        expression.enableEyeTracking = eyes;
                        expression.enableLipSync = lipSync;
                        expression.mouthMorphCancel = cancel;
                    });
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private static bool FlagToggle(bool value)
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(FlagColumnWidth)))
            {
                GUILayout.Space(20);
                return EditorGUILayout.Toggle(value, GUILayout.Width(20), GUILayout.Height(28));
            }
        }

        /// <summary>
        /// 口モーフキャンセラーのシェイプキー。標準は元FXの口モーフキャンセラーのもの。自分で編集すると追加・削除できる。
        /// </summary>
        private void DrawMouthCanceler(ExpressionSet set)
        {
            EditorGUILayout.LabelField("口モーフキャンセラー", EditorStyles.boldLabel);
            var manual = (AnimationClip)EditorGUILayout.ObjectField("対象クリップ（D&D）", set.mouthCancelAnimation, typeof(AnimationClip), false);
            var useValues = EditorGUILayout.ToggleLeft("指定クリップの先頭の値に戻す（オフならベース顔）", set.mouthCancelUseClipValues);
            if (manual != set.mouthCancelAnimation || useValues != set.mouthCancelUseClipValues)
                Modify(set, "口キャンセルの指定", () => { set.mouthCancelAnimation = manual; set.mouthCancelUseClipValues = useValues; });
            EditorGUILayout.LabelField("話している間、口のシェイプキーをベース顔の値に戻し、表情の口とリップシンクの口が重ならないようにします。" +
                                       "表情ごとに止められます（上の一覧）。",
                EditorStyles.wordWrappedMiniLabel);

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
