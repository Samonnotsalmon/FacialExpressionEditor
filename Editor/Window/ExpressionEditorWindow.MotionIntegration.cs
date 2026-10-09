using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private List<MotionIntegration.Candidate> _motionCandidates;
        private GameObject _motionSourceRoot;
        private readonly HashSet<string> _openMotionRules = new HashSet<string>();
        private readonly Dictionary<string, string> _motionSearch = new Dictionary<string, string>();
        private bool _showLegacyContacts;

        private void DrawMotionIntegration(ExpressionSet set)
        {
            EditorGUILayout.LabelField("MMDワールド対応", EditorStyles.boldLabel);
            var entire = EditorGUILayout.Popup("動作方式", set.mmdStopEntireFx ? 1 : 0,
                new[] { "衣装・ギミックを維持（標準）", "FX全体を停止（予備）" }) == 1;
            if (entire != set.mmdStopEntireFx) Modify(set, "MMD対応方式を変更", () => set.mmdStopEntireFx = entire);
            if (!entire)
            {
                var unify = EditorGUILayout.ToggleLeft("ビルド時にFX全体のWrite DefaultsをONに揃える", set.mmdUnifyFxWriteDefaults);
                if (unify != set.mmdUnifyFxWriteDefaults) Modify(set, "MMDのWrite Defaults設定", () => set.mmdUnifyFxWriteDefaults = unify);
                EditorGUILayout.HelpBox("MMD中はこのエディタの表情・瞬き・口キャンセル・顔パーツだけを停止し、衣装などのFXレイヤーを維持します。WD統一はビルド用データだけに適用します。WD OFFの値保持を前提とするギミックは動作が変わる場合があります。統一を外した場合、FXにWD OFFが残ればビルド時に対象を表示して停止します。", MessageType.Info);
            }
            else EditorGUILayout.HelpBox("MMD中はFX全体を停止します。衣装なども初期状態へ戻る場合があります。WDの強制統一は行いません。", MessageType.Info);
            EditorGUILayout.LabelField("ゲーム内の「表情 → 設定 → ダンスギミック有効（MMD対応）」がONかつステーション利用中に動作します。付属ダンスは下の連携ルールで設定します。", EditorStyles.wordWrappedMiniLabel);
            MotionIntegration.Initialize(set, Descriptor);
            EditorGUILayout.LabelField("既存モーションとの連携", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("条件を満たす間、元アバターへ表情制御を譲ります。ルール内の条件はすべて一致（AND）。複数ルールが有効なら、停止対象を合わせて扱います。固定表情・パーツの選択は保持し、解除後は現在の選択へ戻ります。", MessageType.Info);
            if (_motionCandidates == null || _motionSourceRoot != AvatarRoot)
            { _motionCandidates = MotionIntegration.Candidates(Descriptor); _motionSourceRoot = AvatarRoot; }
            var parameters = MotionIntegration.Parameters(Descriptor);
            Undo.RecordObject(set, "既存モーションとの連携を変更");
            EditorGUI.BeginChangeCheck();
            foreach (var rule in set.motionRules.ToList())
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    rule.enabled = EditorGUILayout.Toggle(rule.enabled, GUILayout.Width(18));
                    var open = EditorGUILayout.Foldout(_openMotionRules.Contains(rule.id), rule.name, true);
                    if (open) _openMotionRules.Add(rule.id); else _openMotionRules.Remove(rule.id);
                    if (GUILayout.Button("削除", GUILayout.Width(44))) { set.motionRules.Remove(rule); GUI.changed = true; }
                }
                if (!_openMotionRules.Contains(rule.id))
                {
                    EditorGUILayout.LabelField(string.Join(" ＆ ", rule.conditions.Select(c => $"{c.parameter} {new[] { "オン", "オフ", "＝", "≠", "＞", "＜" }[(int)c.mode]} {((int)c.mode < 2 ? "" : c.value.ToString())}")), EditorStyles.wordWrappedMiniLabel);
                    continue;
                }
                rule.name = EditorGUILayout.TextField("ルール名", rule.name);
                DrawMotionSource(rule);
                EditorGUILayout.LabelField("有効になる条件（すべて一致）", EditorStyles.boldLabel);
                foreach (var condition in rule.conditions.ToList())
                using (new EditorGUILayout.HorizontalScope())
                {
                    var names = parameters.Keys.OrderBy(n => n).ToList();
                    if (!names.Contains(condition.parameter)) names.Insert(0, condition.parameter ?? "");
                    var index = Mathf.Max(0, names.IndexOf(condition.parameter));
                    var next = EditorGUILayout.Popup(index, names.ToArray());
                    if (next != index)
                    {
                        condition.parameter = names[next];
                        condition.mode = parameters[condition.parameter] == AnimatorControllerParameterType.Bool ? MotionConditionMode.IsTrue :
                            parameters[condition.parameter] == AnimatorControllerParameterType.Float ? MotionConditionMode.Greater : MotionConditionMode.Equals;
                    }
                    condition.mode = (MotionConditionMode)EditorGUILayout.Popup((int)condition.mode, new[] { "オン", "オフ", "＝", "≠", "＞", "＜" }, GUILayout.Width(70));
                    if (condition.mode != MotionConditionMode.IsTrue && condition.mode != MotionConditionMode.IsFalse)
                        condition.value = parameters.TryGetValue(condition.parameter, out var type) && type == AnimatorControllerParameterType.Int ? EditorGUILayout.IntField((int)condition.value, GUILayout.Width(65)) : EditorGUILayout.FloatField(condition.value, GUILayout.Width(65));
                    if (GUILayout.Button("×", GUILayout.Width(22))) { rule.conditions.Remove(condition); GUI.changed = true; }
                }
                if (GUILayout.Button("＋ 条件を追加")) { rule.conditions.Add(new MotionCondition()); GUI.changed = true; }
                EditorGUILayout.LabelField("その間に停止する制御", EditorStyles.boldLabel);
                rule.pauseExpressions = EditorGUILayout.ToggleLeft("通常ジェスチャー・固定表情", rule.pauseExpressions);
                rule.pauseBlink = EditorGUILayout.ToggleLeft("まばたき", rule.pauseBlink);
                rule.pauseMouthCancel = EditorGUILayout.ToggleLeft("口変形キャンセル", rule.pauseMouthCancel);
                rule.pauseParts = EditorGUILayout.ToggleLeft("重ねるパーツ", rule.pauseParts);
                using (new EditorGUI.DisabledScope(rule.sourceClips.Count == 0))
                    rule.applyBaseFace = EditorGUILayout.ToggleLeft("連携元クリップにベース顔の補正を反映する", rule.applyBaseFace);
                EditorGUILayout.LabelField(rule.applyBaseFace ? "選択した元クリップに補正を適用します。元アセットは変更しません。" : "連携元の表情をそのまま使います。", EditorStyles.wordWrappedMiniLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ 連携ルール"))
                { var rule = new MotionIntegrationRule(); set.motionRules.Add(rule); _openMotionRules.Add(rule.id); GUI.changed = true; }
                if (GUILayout.Button("＋ AFKのルール"))
                {
                    var rule = new MotionIntegrationRule { name = "AFK", conditions = new List<MotionCondition> { new MotionCondition() }, sourceClips = FxAnalysis.AfkClips.ToList(), applyBaseFace = set.applyBaseFaceToAfk && FxAnalysis.AfkClips.Count > 0 };
                    set.motionRules.Add(rule); _openMotionRules.Add(rule.id); GUI.changed = true;
                }
            }
            if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(set); _faceDefaults = null; InvalidateDetailPreview(); }
            foreach (var error in MotionIntegration.Validate(set, Descriptor, set.motionRules)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void DrawMotionSource(MotionIntegrationRule rule)
        {
            _motionSearch.TryGetValue(rule.id, out var search);
            _motionSearch[rule.id] = EditorGUILayout.TextField("元ステートを検索", search ?? "");
            var candidates = _motionCandidates.Where(c => string.IsNullOrWhiteSpace(_motionSearch[rule.id]) || c.Label.IndexOf(_motionSearch[rule.id], System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var selected = _motionCandidates.Find(c => c.playable == rule.sourcePlayable && c.layer == rule.sourceLayer && c.state == rule.sourceState);
            if (selected != null && !candidates.Contains(selected)) candidates.Insert(0, selected);
            var current = selected == null ? 0 : candidates.IndexOf(selected) + 1;
            var next = EditorGUILayout.Popup("条件の参照元", current, new[] { "手動設定／参照元なし" }.Concat(candidates.Select(c => c.Label)).ToArray());
            if (next != current)
            {
                selected = next == 0 ? null : candidates[next - 1];
                rule.sourcePlayable = selected?.playable ?? "FX"; rule.sourceLayer = selected?.layer; rule.sourceState = selected?.state;
                rule.sourceClips = selected?.clips.ToList() ?? new List<AnimationClip>();
                GUI.changed = true;
            }
            if (selected == null)
            {
                if (rule.sourceClips.Count > 0) EditorGUILayout.LabelField("対象の元クリップ", string.Join("、", rule.sourceClips.Where(c => c != null).Select(c => c.name)), EditorStyles.wordWrappedMiniLabel);
                return;
            }
            EditorGUILayout.LabelField("元クリップ", string.Join("、", selected.clips.Select(c => c.name)), EditorStyles.wordWrappedMiniLabel);
            for (var i = 0; i < selected.entries.Count; i++)
            {
                var conditions = selected.entries[i];
                EditorGUILayout.LabelField("入口 " + (i + 1), DescribeConditions(conditions), EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(conditions.Length == 0))
                if (GUILayout.Button("この入口条件を候補として取り込む"))
                { rule.conditions = conditions.Select(MotionIntegration.FromAnimator).ToList(); GUI.changed = true; }
            }
            foreach (var exit in selected.exits) EditorGUILayout.LabelField("出口", DescribeConditions(exit), EditorStyles.wordWrappedMiniLabel);
            foreach (var change in selected.parameterChanges) EditorGUILayout.LabelField("ステート内の値変更", change, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.HelpBox("入口条件が再生中も成立するか確認してください。終了時間・親ステートの条件・Parameter Driverによる変更は、この条件一覧だけでは判断できません。" + (selected.timedExit ? " このステートには終了時間による遷移があります。" : ""), MessageType.Info);
        }
        private static string DescribeConditions(UnityEditor.Animations.AnimatorCondition[] conditions)
        {
            return conditions.Length == 0 ? "条件なし" : string.Join(" ＆ ", conditions.Select(c => $"{c.parameter} {c.mode} {c.threshold}"));
        }
    }
}
