using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情データの仮インスペクタ。専用ウィンドウ（Phase 3）ができるまでの編集用。
    /// </summary>
    [CustomEditor(typeof(ExpressionSet))]
    internal class ExpressionSetEditor : UnityEditor.Editor
    {
        private static readonly string[] GestureLabels =
            { "ニュートラル", "グー（Fist）", "パー", "指差し", "ピース", "ロック", "銃", "サムズアップ" };

        private static readonly string[] HandLabels = { "左手", "右手" };

        private bool _showSets = true;
        private bool _showExpressions;
        private bool _showParts = true;
        private bool _showAdvanced;
        private readonly HashSet<string> _openSets = new HashSet<string>();
        private readonly HashSet<string> _openCombos = new HashSet<string>();
        private readonly HashSet<string> _openPartProperties = new HashSet<string>();

        public override void OnInspectorGUI()
        {
            var set = (ExpressionSet)target;

            // 変更が無ければUndoには積まれない。
            Undo.RecordObject(set, "表情セットを編集");
            EditorGUI.BeginChangeCheck();

            set.defaultTransitionDuration = EditorGUILayout.FloatField("遷移時間（秒）", set.defaultTransitionDuration);

            EditorGUILayout.Space();
            DrawGestureSets(set);
            EditorGUILayout.Space();
            DrawParts(set);
            EditorGUILayout.Space();
            DrawExpressions(set);
            EditorGUILayout.Space();
            DrawAdvanced(set);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(set);
            }
        }

        private void DrawGestureSets(ExpressionSet set)
        {
            _showSets = EditorGUILayout.Foldout(_showSets, $"表情セット（{set.gestureSets.Count}）", true, EditorStyles.foldoutHeader);
            if (!_showSets) return;

            EditorGUILayout.HelpBox(
                "「ジェスチャーで使う」セットが2つ以上あると、メニューで切り替えられます。\n" +
                "「固定メニューに並べる」セットの表情は、表情固定メニューから選べます。", MessageType.None);

            var (ids, labels) = ExpressionOptions(set);

            foreach (var gestureSet in set.gestureSets.ToList())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var open = _openSets.Contains(gestureSet.id);
                        var next = EditorGUILayout.Foldout(open, GUIContent.none, true);
                        if (next != open)
                        {
                            if (next) _openSets.Add(gestureSet.id);
                            else _openSets.Remove(gestureSet.id);
                        }

                        gestureSet.name = EditorGUILayout.TextField(gestureSet.name);
                        if (GUILayout.Button("削除", GUILayout.Width(44)))
                        {
                            set.gestureSets.Remove(gestureSet);
                            EditorUtility.SetDirty(set);
                            GUIUtility.ExitGUI();
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        gestureSet.useForGesture = EditorGUILayout.ToggleLeft("ジェスチャーで使う", gestureSet.useForGesture);
                        gestureSet.showInFixedMenu = EditorGUILayout.ToggleLeft("固定メニューに並べる", gestureSet.showInFixedMenu);
                    }

                    if (_openSets.Contains(gestureSet.id)) DrawMapping(gestureSet, ids, labels);
                }
            }

            if (GUILayout.Button("表情セットを追加"))
            {
                var gestureSet = new GestureSet { name = $"セット{set.gestureSets.Count + 1}" };
                set.gestureSets.Add(gestureSet);
                _openSets.Add(gestureSet.id);
            }
        }

        private void DrawMapping(GestureSet gestureSet, string[] ids, string[] labels)
        {
            var mapping = gestureSet.mapping;
            mapping.EnsureSize();

            mapping.dominantHand = (Hand)EditorGUILayout.Popup("優先する手", (int)mapping.dominantHand, HandLabels);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("", GUILayout.Width(EditorGUIUtility.labelWidth - 4));
                EditorGUILayout.LabelField("左手", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("右手", EditorStyles.miniBoldLabel);
            }

            for (var i = 0; i < GestureMapping.GestureCount; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel(GestureLabels[i]);
                    mapping.left[i] = ExpressionPopup(mapping.left[i], ids, labels);
                    mapping.right[i] = ExpressionPopup(mapping.right[i], ids, labels);
                }
            }

            var open = _openCombos.Contains(gestureSet.id);
            var next = EditorGUILayout.Foldout(open, $"組み合わせの上書き（{mapping.combos.Count}）", true);
            if (next != open)
            {
                if (next) _openCombos.Add(gestureSet.id);
                else _openCombos.Remove(gestureSet.id);
            }
            if (!next) return;

            foreach (var combo in mapping.combos.ToList())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    combo.left = (HandGesture)EditorGUILayout.Popup((int)combo.left, GestureLabels);
                    GUILayout.Label("×", GUILayout.Width(14));
                    combo.right = (HandGesture)EditorGUILayout.Popup((int)combo.right, GestureLabels);
                    combo.expressionId = ExpressionPopup(combo.expressionId, ids, labels);
                    if (GUILayout.Button("削除", GUILayout.Width(44)))
                    {
                        mapping.combos.Remove(combo);
                        EditorUtility.SetDirty(target);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (GUILayout.Button("組み合わせを追加"))
            {
                mapping.combos.Add(new GestureComboOverride());
            }
        }

        private void DrawParts(ExpressionSet set)
        {
            _showParts = EditorGUILayout.Foldout(_showParts, $"パーツ（{set.parts.Count}）", true, EditorStyles.foldoutHeader);
            if (!_showParts) return;

            EditorGUILayout.HelpBox(
                "ゲーム中にメニューから表情へ重ねて出せます。同じ「排他グループ」のパーツは同時に出ません。", MessageType.None);

            foreach (var part in set.parts.ToList())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        part.name = EditorGUILayout.TextField(part.name);
                        if (GUILayout.Button("削除", GUILayout.Width(44)))
                        {
                            set.parts.Remove(part);
                            EditorUtility.SetDirty(set);
                            GUIUtility.ExitGUI();
                        }
                    }

                    var clip = (AnimationClip)EditorGUILayout.ObjectField("クリップ", part.clip, typeof(AnimationClip), false);
                    if (clip != part.clip)
                    {
                        part.clip = clip;
                        part.properties = FxImporter.DefaultPartProperties(clip);
                    }

                    part.exclusiveGroup = EditorGUILayout.TextField("排他グループ", part.exclusiveGroup);
                    DrawPartProperties(part);
                }
            }

            var dropped = (AnimationClip)EditorGUILayout.ObjectField("クリップを追加", null, typeof(AnimationClip), false);
            if (dropped != null)
            {
                set.parts.Add(new FacialPart
                {
                    name = dropped.name,
                    clip = dropped,
                    properties = FxImporter.DefaultPartProperties(dropped),
                });
            }
        }

        private void DrawPartProperties(FacialPart part)
        {
            if (part.clip == null) return;

            var floatBindings = AnimationUtility.GetCurveBindings(part.clip);
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(part.clip);
            var total = floatBindings.Length + objectBindings.Length;

            var open = _openPartProperties.Contains(part.id);
            var next = EditorGUILayout.Foldout(open, $"動かすプロパティ（{part.properties.Count} / {total}）", true);
            if (next != open)
            {
                if (next) _openPartProperties.Add(part.id);
                else _openPartProperties.Remove(part.id);
            }
            if (!next) return;

            EditorGUI.indentLevel++;
            foreach (var binding in floatBindings)
            {
                var curve = AnimationUtility.GetEditorCurve(part.clip, binding);
                var value = curve != null && curve.keys.Length > 0 ? curve.keys.Max(k => k.value) : 0;
                PropertyToggle(part, binding, $"{ShortName(binding)}  ({value:0.##})");
            }
            foreach (var binding in objectBindings)
            {
                PropertyToggle(part, binding, ShortName(binding));
            }
            EditorGUI.indentLevel--;
        }

        private static void PropertyToggle(FacialPart part, EditorCurveBinding binding, string label)
        {
            var key = ExpressionClipBuilder.Key(binding);
            var on = part.properties.Contains(key);
            var next = EditorGUILayout.ToggleLeft(label, on);
            if (next && !on) part.properties.Add(key);
            if (!next && on) part.properties.Remove(key);
        }

        private static string ShortName(EditorCurveBinding binding)
        {
            var property = binding.propertyName.StartsWith("blendShape.")
                ? binding.propertyName.Substring("blendShape.".Length)
                : $"{binding.type.Name}.{binding.propertyName}";
            return string.IsNullOrEmpty(binding.path) ? property : $"{binding.path} / {property}";
        }

        private void DrawExpressions(ExpressionSet set)
        {
            _showExpressions = EditorGUILayout.Foldout(_showExpressions, $"表情（{set.expressions.Count}）", true, EditorStyles.foldoutHeader);
            if (!_showExpressions) return;

            foreach (var expression in set.expressions.ToList())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        expression.name = EditorGUILayout.TextField(expression.name);
                        if (GUILayout.Button("削除", GUILayout.Width(44)))
                        {
                            RemoveExpression(set, expression);
                            GUIUtility.ExitGUI();
                        }
                    }

                    expression.clip = (AnimationClip)EditorGUILayout.ObjectField("クリップ", expression.clip, typeof(AnimationClip), false);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        expression.overrideTransitionDuration = EditorGUILayout.ToggleLeft("遷移時間を個別に設定",
                            expression.overrideTransitionDuration, GUILayout.Width(EditorGUIUtility.labelWidth));
                        using (new EditorGUI.DisabledScope(!expression.overrideTransitionDuration))
                        {
                            expression.transitionDuration = EditorGUILayout.FloatField(
                                expression.overrideTransitionDuration ? expression.transitionDuration : set.defaultTransitionDuration);
                        }
                    }
                }
            }

            var dropped = (AnimationClip)EditorGUILayout.ObjectField("クリップから追加", null, typeof(AnimationClip), false);
            if (dropped != null)
            {
                set.expressions.Add(new Expression { name = dropped.name, clip = dropped });
            }

            if (GUILayout.Button("表情を追加"))
            {
                set.expressions.Add(new Expression { name = "新しい表情" });
            }
        }

        private void DrawAdvanced(ExpressionSet set)
        {
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "詳細", true, EditorStyles.foldoutHeader);
            if (!_showAdvanced) return;

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ExpressionSet.originalGestureLayers)),
                new GUIContent("置き換える元FXレイヤー（ジェスチャー）"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ExpressionSet.originalPartLayers)),
                new GUIContent("置き換える元FXレイヤー（パーツ）"), true);
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.LabelField("ライブラリのフォルダ", EditorStyles.miniBoldLabel);
            foreach (var guid in set.libraryFolderGuids.ToList())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(path) ? "（見つかりません）" : path);
                    if (GUILayout.Button("削除", GUILayout.Width(44)))
                    {
                        set.libraryFolderGuids.Remove(guid);
                        EditorUtility.SetDirty(set);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            var folder = EditorGUILayout.ObjectField("フォルダを追加", null, typeof(DefaultAsset), false);
            if (folder != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder)))
            {
                FxImporter.AddFolder(set, folder);
            }
        }

        private static (string[] ids, string[] labels) ExpressionOptions(ExpressionSet set)
        {
            var ids = new[] { "" }.Concat(set.expressions.Select(e => e.id)).ToArray();
            var labels = new[] { "（なし）" }
                .Concat(set.expressions.Select((e, i) => $"{i + 1}. {(string.IsNullOrEmpty(e.name) ? "（名前なし）" : e.name)}"))
                .ToArray();
            return (ids, labels);
        }

        private static string ExpressionPopup(string current, string[] ids, string[] labels)
        {
            var index = System.Array.IndexOf(ids, current ?? "");
            if (index < 0) index = 0;
            var next = EditorGUILayout.Popup(index, labels);
            return next == 0 ? null : ids[next];
        }

        private static void RemoveExpression(ExpressionSet set, Expression expression)
        {
            set.expressions.Remove(expression);
            foreach (var mapping in set.gestureSets.Select(s => s.mapping))
            {
                mapping.EnsureSize();
                for (var i = 0; i < GestureMapping.GestureCount; i++)
                {
                    if (mapping.left[i] == expression.id) mapping.left[i] = null;
                    if (mapping.right[i] == expression.id) mapping.right[i] = null;
                }
                mapping.combos.RemoveAll(c => c.expressionId == expression.id);
            }
            EditorUtility.SetDirty(set);
        }
    }
}
