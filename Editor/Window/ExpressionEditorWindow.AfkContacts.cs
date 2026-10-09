using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 「AFK・コンタクト」タブ。元FXのAFKの顔、コンタクト・PhysBoneで出す表情、顔を動かしている元FXのレイヤーの扱い。
    /// </summary>
    internal partial class ExpressionEditorWindow
    {
        private const float ContactRowHeight = 88f;
        private const float OriginalCellSize = 72f;
        private static readonly string[] LayerModeLabels = { "そのまま", "顔のカーブだけ取り除く", "使わない" };

        // 元FXの顔に関わるレイヤー。元FXを調べるので、FaceDefaults を作り直したときだけ作り直す。
        private OriginalFxAnalysis _fxAnalysis;
        private AvatarFaceDefaults _fxAnalysisFor;
        private OriginalFxAnalysis FxAnalysis
        {
            get
            {
                var defaults = FaceDefaults;
                if (_fxAnalysis != null && _fxAnalysisFor == defaults) return _fxAnalysis;

                var handled = defaults.MouthCancelerLayers.ToList();
                if (defaults.BlinkLayer != null) handled.Add(defaults.BlinkLayer);
                _fxAnalysisFor = defaults;
                return _fxAnalysis = OriginalFxAnalysis.Analyze(Descriptor, Set, Variant, handled);
            }
        }

        private void DrawAfkContactsTab(ExpressionSet set)
        {
            DrawMotionIntegration(set);
            EditorGUILayout.Space(12);
            DrawAfk();
            EditorGUILayout.Space(12);
            _showLegacyContacts = EditorGUILayout.Foldout(_showLegacyContacts, "追加設定：コンタクト・PhysBone", true);
            if (_showLegacyContacts) DrawContacts(set);
            EditorGUILayout.Space(12);
            DrawOriginalFaceLayers(set);
        }

        private void DrawAfk()
        {
            EditorGUILayout.LabelField("AFK", EditorStyles.boldLabel);
            var apply = EditorGUILayout.ToggleLeft("AFKクリップにベース顔の補正を反映する（既定）", Set.applyBaseFaceToAfk);
            if (apply != Set.applyBaseFaceToAfk) Modify(Set, "AFKのベース補正", () => Set.applyBaseFaceToAfk = apply);
            EditorGUILayout.LabelField("連携ルールで選んだクリップは、そのルールの補正指定を優先します。", EditorStyles.wordWrappedMiniLabel);
            var analysis = FxAnalysis;
            if (analysis.AfkLayers.Count == 0)
            {
                EditorGUILayout.LabelField("元FXに、顔を動かすAFKのアニメーションはありません。", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            EditorGUILayout.LabelField($"元FXの「{string.Join("」「", analysis.AfkLayers)}」レイヤーのAFKの顔を確認・調整できます。" +
                                       "「AFKの顔を編集」で、時間ごとにキーを打って、この顔だけの動きにできます（目を閉じている間はベース顔を0にする、など）。",
                EditorStyles.wordWrappedMiniLabel);

            var variant = Variant;
            if (variant == null)
            {
                EditorGUILayout.LabelField("顔バリアントが無いので、元のまま使います。", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            var count = variant.afkCurves.Where(c => analysis.AfkClips.Contains(c.clip)).Sum(c => c.curves.Count);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(count > 0 ? $"この顔だけの動き：シェイプキー {count} 件" : "この顔だけの動き：なし", EditorStyles.miniLabel);
                if (GUILayout.Button("AFKの顔を編集", GUILayout.Width(120)))
                {
                    ExpressionClipEditorWindow.OpenAfk(_avatar);
                }
            }
        }

        /// <summary>
        /// コンタクト・PhysBoneで出す表情。表情選択（固定）より下、ジェスチャーより上で、上にあるものほど優先する。
        /// </summary>
        private void DrawContacts(ExpressionSet set)
        {
            EditorGUILayout.LabelField("コンタクト・PhysBone", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("アバターに元からあるコンタクト・PhysBoneのパラメータが条件を満たしている間、表情を出します。" +
                                       "表情選択（固定）より下、ジェスチャーより上で、上にあるものほど優先します。",
                EditorStyles.wordWrappedMiniLabel);

            var candidates = OriginalFxAnalysis.ContactParameters(Descriptor);
            if (candidates.Count == 0 && set.contactTriggers.Count == 0)
            {
                EditorGUILayout.HelpBox("このアバターには、パラメータを設定したコンタクト・PhysBoneがありません。", MessageType.Info);
                return;
            }

            var width = CenterWidth;
            for (var i = 0; i < set.contactTriggers.Count; i++)
            {
                var trigger = set.contactTriggers[i];
                var row = GUILayoutUtility.GetRect(width, ContactRowHeight + 4);
                if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(row, new Color(0, 0, 0, 0.08f));
                DrawContactRow(set, trigger, i, candidates, new Rect(row.x, row.y, width, ContactRowHeight));
            }

            using (new EditorGUI.DisabledScope(candidates.Count == 0))
            {
                if (GUILayout.Button("コンタクト・PhysBoneを追加", GUILayout.Width(200)))
                {
                    var first = candidates[0];
                    Modify(set, "コンタクト・PhysBoneを追加", () => set.contactTriggers.Add(new ContactTrigger
                    {
                        parameter = first.parameter,
                        isFloat = first.isFloat,
                    }));
                }
            }
        }

        private void DrawContactRow(ExpressionSet set, ContactTrigger trigger, int index,
            List<(string parameter, bool isFloat, string source)> candidates, Rect rect)
        {
            const float slotWidth = 220f;
            var left = new Rect(rect.x + 4, rect.y + 4, rect.width - slotWidth - 16, 18);

            // パラメータ（見つからないものも選んだまま出す）。
            var options = candidates.Select(c => $"{c.parameter}（{c.source}）").ToList();
            var index2 = candidates.FindIndex(c => c.parameter == trigger.parameter);
            if (index2 < 0)
            {
                options.Insert(0, $"{trigger.parameter}（見つかりません）");
                index2 = 0;
            }
            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.Popup(left, $"{index + 1}. パラメータ", index2, options.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                var offset = candidates.FindIndex(c => c.parameter == trigger.parameter) < 0 ? 1 : 0;
                if (next - offset >= 0)
                {
                    var chosen = candidates[next - offset];
                    Modify(set, "コンタクト・PhysBoneを変更", () =>
                    {
                        trigger.parameter = chosen.parameter;
                        trigger.isFloat = chosen.isFloat;
                    });
                }
            }

            // 条件（Bool はオンの間、Float はしきい値より大きい間）。
            var conditionRect = new Rect(left.x, left.yMax + 4, left.width, 18);
            if (trigger.isFloat)
            {
                EditorGUI.BeginChangeCheck();
                var threshold = EditorGUI.Slider(conditionRect, "この値より大きい間", trigger.threshold, 0, 1);
                if (EditorGUI.EndChangeCheck()) Modify(set, "しきい値を変更", () => trigger.threshold = threshold);
            }
            else
            {
                EditorGUI.LabelField(conditionRect, "条件", "オンの間");
            }

            var buttons = new Rect(left.x, conditionRect.yMax + 8, 24, 20);
            if (GUI.Button(buttons, "↑") && index > 0)
            {
                Modify(set, "コンタクト・PhysBoneを並べ替え", () => Swap(set.contactTriggers, index, index - 1));
                GUIUtility.ExitGUI();
            }
            buttons.x += 26;
            if (GUI.Button(buttons, "↓") && index < set.contactTriggers.Count - 1)
            {
                Modify(set, "コンタクト・PhysBoneを並べ替え", () => Swap(set.contactTriggers, index, index + 1));
                GUIUtility.ExitGUI();
            }
            buttons.x += 26;
            buttons.width = 44;
            if (GUI.Button(buttons, "削除"))
            {
                Modify(set, "コンタクト・PhysBoneを削除", () => set.contactTriggers.Remove(trigger));
                MarkLibraryDirty();
                GUIUtility.ExitGUI();
            }

            DrawSlot(new Rect(rect.xMax - slotWidth - 4, rect.y + 2, slotWidth, ContactRowHeight - 4), set, trigger.expressionId,
                id => trigger.expressionId = id, "コンタクト・PhysBone");
        }

        private static void Swap<T>(List<T> list, int a, int b)
        {
            (list[a], list[b]) = (list[b], list[a]);
        }

        /// <summary>
        /// 置き換えていないのに顔を動かしている元FXのレイヤー（AFK以外）。そのまま／顔のカーブだけ取り除く／使わない、を選ぶ。
        /// </summary>
        private void DrawOriginalFaceLayers(ExpressionSet set)
        {
            EditorGUILayout.LabelField("顔を動かしている元FXのレイヤー", EditorStyles.boldLabel);
            var layers = FxAnalysis.FaceLayers;
            if (layers.Count == 0)
            {
                EditorGUILayout.LabelField("置き換えていないのに顔を動かしているレイヤーはありません。", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            EditorGUILayout.LabelField("コンタクトやPhysBone、メニューで出る元のアバターの表情です。サムネイルを選ぶと、右のプレビューで確かめられます。" +
                                       "表情とぶつかるときは、顔のカーブだけ取り除くか（ほかの演出は残ります）、レイヤーごと使わないようにできます。",
                EditorStyles.wordWrappedMiniLabel);
            var analysis = FxAnalysis;
            foreach (var name in layers)
            {
                EditorGUILayout.Space(4);
                var triggers = analysis.FaceLayerTriggers.TryGetValue(name, out var list) ? list : new List<OriginalFxAnalysis.FaceLayerClip>();
                using (new EditorGUILayout.HorizontalScope())
                {
                    // 顔を動かすクリップのサムネイル（選ぶと右のプレビューに出す）。
                    foreach (var trigger in triggers)
                    {
                        var rect = GUILayoutUtility.GetRect(OriginalCellSize, OriginalCellSize + 16, GUILayout.Width(OriginalCellSize));
                        if (DrawCell(rect, ClipThumbnail(trigger.Clip), trigger.Clip.name, IsSelected(SelectionKind.OriginalClip, name, trigger.Clip)))
                        {
                            Select(SelectionKind.OriginalClip, name, trigger.Clip);
                        }
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(name, EditorStyles.boldLabel);
                        foreach (var trigger in triggers.Where(t => !string.IsNullOrEmpty(t.Parameter)))
                        {
                            EditorGUILayout.LabelField($"{trigger.Clip.name}：{TriggerText(trigger)}", EditorStyles.wordWrappedMiniLabel);
                        }

                        var setting = set.originalLayerSettings.Find(s => s.layerName == name);
                        var mode = setting != null ? setting.mode : OriginalLayerMode.Keep;
                        EditorGUI.BeginChangeCheck();
                        var next = (OriginalLayerMode)EditorGUILayout.Popup("ビルドでの扱い", (int)mode, LayerModeLabels);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Modify(set, "元FXのレイヤーの扱いを変更", () =>
                            {
                                set.originalLayerSettings.RemoveAll(s => s.layerName == name);
                                if (next != OriginalLayerMode.Keep) set.originalLayerSettings.Add(new OriginalLayerSetting { layerName = name, mode = next });
                            });
                        }
                    }
                }
            }
        }
    }
}
