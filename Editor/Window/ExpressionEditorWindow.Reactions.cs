using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private List<ReactionImporter.Candidate> _reactionCandidates;
        private AvatarFaceDefaults _reactionDefaults;
        private readonly HashSet<string> _openReactionLayers = new HashSet<string>();
        private List<ReactionImporter.Candidate> ReactionCandidates
        {
            get
            {
                if (_reactionCandidates == null || _reactionDefaults != FaceDefaults)
                { _reactionDefaults = FaceDefaults; _reactionCandidates = ReactionImporter.Scan(Descriptor, Set, Variant); }
                return _reactionCandidates;
            }
        }

        private void DrawReactionsTab(ExpressionSet set)
        {
            EditorGUILayout.HelpBox("コンタクト・PhysBoneを直接使う表情だけを表示します。瞬き・口モーフキャンセル、別レイヤーの耳・尻尾は対象外です。元FXの条件・遷移・優先順位・ON/OFF設定をそのまま使い、クリップだけを編集・差し替えます。不要な反応は「アバター設定 → 置き換える元FXレイヤー」で除外できます。", MessageType.None);
            if (GUILayout.Button("元FXを再読み込み", GUILayout.Width(150))) { _reactionCandidates = null; _faceDefaults = null; }
            var fx = FxImporter.GetFx(Descriptor);
            foreach (var candidate in ReactionCandidates)
            {
                EditorGUILayout.LabelField(candidate.Name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(string.Join("、", candidate.Parameters), EditorStyles.wordWrappedMiniLabel);
                foreach (var layerName in candidate.Layers)
                {
                    var layers = fx.layers.Where(l => l.name == layerName).ToList();
                    if (layers.Count != 1) { EditorGUILayout.HelpBox(layerName + "：同名のレイヤーが複数あるため、差し替え対象を特定できません。", MessageType.Warning); continue; }
                    if (layers[0].syncedLayerIndex >= 0 || fx.layers.Any(l => l.name != layerName && l.stateMachine == layers[0].stateMachine))
                    { EditorGUILayout.HelpBox(layerName + "：同期・共有ステートマシンのため個別差し替えはできません。元FXをそのまま使用します。", MessageType.Info); continue; }
                    var removed = set.originalGestureLayers.Contains(layerName) || set.originalPartLayers.Contains(layerName) ||
                        set.originalLayerSettings.Any(s => s.layerName == layerName && s.mode == OriginalLayerMode.Disable);
                    var open = EditorGUILayout.Foldout(_openReactionLayers.Contains(layerName), layerName + (removed ? "（置き換え対象・元の反応は削除）" : ""), true);
                    if (open) _openReactionLayers.Add(layerName); else _openReactionLayers.Remove(layerName);
                    if (!open) continue;
                    using (new EditorGUI.DisabledScope(removed))
                    foreach (var source in FxImporter.AllStates(layers[0].stateMachine).SelectMany(s => ReactionImporter.Clips(s.motion)).Distinct())
                    {
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            var entry = set.reactionClips.Find(c => c.layerName == layerName && c.source == source);
                            var expression = entry != null ? set.FindExpression(entry.expressionId) : null;
                            var shown = expression?.clip ?? source;
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                var rect = GUILayoutUtility.GetRect(72, 88, GUILayout.Width(72));
                                if (DrawCell(rect, ClipThumbnail(shown), shown.name, IsSelected(SelectionKind.OriginalClip, layerName, shown)))
                                    Select(SelectionKind.OriginalClip, layerName, shown);
                                using (new EditorGUILayout.VerticalScope())
                                {
                                    EditorGUILayout.LabelField(source.name, EditorStyles.boldLabel);
                                    var replacement = (AnimationClip)EditorGUILayout.ObjectField("使用するクリップ", shown, typeof(AnimationClip), false);
                                    if (replacement != null && replacement != shown)
                                    {
                                        expression = ReactionImporter.EditClip(set, layerName, source, replacement, AvatarRoot);
                                        MarkLibraryDirty(); InvalidateDetailPreview();
                                    }
                                    using (new EditorGUILayout.HorizontalScope())
                                    {
                                        if (GUILayout.Button(expression == null ? "複製して編集" : "編集"))
                                        {
                                            expression = ReactionImporter.EditClip(set, layerName, source, null, AvatarRoot);
                                            MarkLibraryDirty(); ExpressionClipEditorWindow.Open(_avatar, expression);
                                        }
                                        using (new EditorGUI.DisabledScope(entry == null))
                                        if (GUILayout.Button("元に戻す", GUILayout.Width(80)))
                                        {
                                            Modify(set, "反応を元クリップに戻す", () => set.reactionClips.Remove(entry));
                                            MarkLibraryDirty(); InvalidateDetailPreview(); GUIUtility.ExitGUI();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (ReactionCandidates.Count == 0) EditorGUILayout.LabelField("顔に関係するコンタクト・PhysBoneの反応は見つかりませんでした。", EditorStyles.wordWrappedMiniLabel);
            var knownLayers = new HashSet<string>(ReactionCandidates.SelectMany(r => r.Layers));
            var orphaned = set.reactionClips.Where(c => fx == null || !knownLayers.Contains(c.layerName) ||
                !fx.layers.Where(l => l.name == c.layerName).SelectMany(l => FxImporter.AllStates(l.stateMachine)).SelectMany(s => ReactionImporter.Clips(s.motion)).Contains(c.source)).ToList();
            foreach (var entry in orphaned)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("現在の反応対象外／元FXで見つからない差し替え：" + entry.layerName, EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("差し替えを解除", GUILayout.Width(110)))
                    { Modify(set, "古い反応の差し替えを解除", () => set.reactionClips.Remove(entry)); MarkLibraryDirty(); GUIUtility.ExitGUI(); }
                }
            // Keep old explicit settings editable, but do not offer a second creation workflow.
            if (set.contactTriggers.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("以前に登録した簡易反応", EditorStyles.boldLabel);
                DrawContacts(set);
            }
        }
    }
}
