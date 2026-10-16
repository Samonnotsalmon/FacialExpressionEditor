using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private List<string> _motionSetupWarnings = new List<string>();
        private List<string> _legacyReactionWarnings = new List<string>();
        private string _menuSetupWarning;
        private string _writeDefaultsWarning;

        private void DrawBuildSetupWarnings(ExpressionSet set)
        {
            if (Event.current.type == EventType.Layout)
            {
                _motionSetupWarnings = FaceControlPlan.MotionSetupWarnings(set, Descriptor, FaceDefaults);
                _menuSetupWarning = null;
                // BuildPlan normalizes gesture arrays. Validate a copy so drawing does not modify user data.
                var copy = Instantiate(set);
                try { BuildPlan.Create(copy); }
                catch (InvalidOperationException e) { _menuSetupWarning = e.Message; }
                finally { DestroyImmediate(copy); }
                _legacyReactionWarnings.Clear();
                var fx = FxImporter.GetFx(Descriptor);
                var sourceLayers = fx != null ? fx.layers : new UnityEditor.Animations.AnimatorControllerLayer[0];
                foreach (var group in set.reactionClips.GroupBy(c => c.layerName))
                {
                    if (set.originalGestureLayers.Contains(group.Key) || set.originalPartLayers.Contains(group.Key) ||
                        set.originalLayerSettings.Any(s => s.layerName == group.Key && s.mode == OriginalLayerMode.Disable)) continue;
                    var layers = sourceLayers.Where(l => l.name == group.Key).ToList();
                    if (layers.Count != 1)
                    { _legacyReactionWarnings.Add("以前の反応設定の元レイヤーが見つからないか、同名で重複しています：" + group.Key); continue; }
                    if (layers[0].syncedLayerIndex >= 0 || sourceLayers.Any(l => l != layers[0] && l.stateMachine == layers[0].stateMachine))
                        _legacyReactionWarnings.Add("以前の反応設定が同期・共有ステートマシンを参照しています：" + group.Key);
                    var clips = FxImporter.AllStates(layers[0].stateMachine).SelectMany(s => ReactionImporter.Clips(s.motion)).ToList();
                    foreach (var mapping in group)
                    {
                        if (set.FindExpression(mapping.expressionId)?.clip == null)
                            _legacyReactionWarnings.Add("以前の反応設定の差し替えクリップが未指定・参照切れです：" + group.Key);
                        if (mapping.source == null || !clips.Contains(mapping.source))
                            _legacyReactionWarnings.Add("以前の反応設定の元クリップが見つかりません：" + group.Key);
                    }
                }
                _writeDefaultsWarning = null;
                if (!set.mmdStopEntireFx && !set.mmdUnifyFxWriteDefaults && fx != null)
                {
                    var removed = new HashSet<string>(set.originalGestureLayers.Concat(set.originalPartLayers)
                        .Concat(FaceDefaults.MouthCancelerLayers).Concat(new[] { FaceDefaults.BlinkLayer })
                        .Concat(set.originalLayerSettings.Where(s => s.mode == OriginalLayerMode.Disable).Select(s => s.layerName)));
                    var off = fx.layers.Where(l => !removed.Contains(l.name)).SelectMany(l => FxImporter.AllStates(l.stateMachine)
                        .Where(s => !s.writeDefaultValues).Select(s => l.name + " / " + s.name)).Take(3).ToList();
                    if (off.Count > 0) _writeDefaultsWarning = "Write Defaultsの一括ONが無効で、残す元FXにWD OFFがあります。ビルド後も残る場合はビルドが停止します。\n" + string.Join("\n", off);
                }
            }
            if (_motionSetupWarnings.Count > 0)
            {
                UnityEditor.EditorGUILayout.HelpBox("モーション連携に未設定・不整合があります。このままではビルドできません。\n" + string.Join("\n", _motionSetupWarnings.Distinct()), UnityEditor.MessageType.Warning);
                if (_tab != Tab.AfkContacts && GUILayout.Button("モーション連携を開く", GUILayout.Width(190)))
                { _tab = Tab.AfkContacts; _centerScroll = Vector2.zero; }
            }
            if (_writeDefaultsWarning != null)
            {
                UnityEditor.EditorGUILayout.HelpBox(_writeDefaultsWarning, UnityEditor.MessageType.Warning);
                if (_tab != Tab.AfkContacts && GUILayout.Button("MMDの設定を開く", GUILayout.Width(190)))
                { _tab = Tab.AfkContacts; _centerScroll = Vector2.zero; }
            }
            if (_menuSetupWarning != null)
            {
                UnityEditor.EditorGUILayout.HelpBox(_menuSetupWarning, UnityEditor.MessageType.Warning);
                if (_tab != Tab.Menu && GUILayout.Button("メニュー・ジェスチャーを開く", GUILayout.Width(220)))
                { _tab = Tab.Menu; _centerScroll = Vector2.zero; }
            }
            if (_legacyReactionWarnings.Count > 0)
            {
                UnityEditor.EditorGUILayout.HelpBox(string.Join("\n", _legacyReactionWarnings.Distinct()) + "\n反応タブは現在非表示です。設定データの以前の差し替え設定を確認してください。", UnityEditor.MessageType.Warning);
                if (GUILayout.Button("設定データを選択", GUILayout.Width(190))) UnityEditor.Selection.activeObject = set;
            }
        }
    }
}
