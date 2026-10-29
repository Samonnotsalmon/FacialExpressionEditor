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

        private bool _showAfkDetails;
        private readonly HashSet<string> _openMotionDetails = new HashSet<string>();

        private static bool IsAfkRule(MotionIntegrationRule rule) => rule.conditions.Any(c => c.parameter == "AFK");

        private void DrawMotionIntegration(ExpressionSet set)
        {
            EditorGUILayout.LabelField("元の表情を優先する場面", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("ダンスやAFK中に、このエディタの制御を一時停止して元アバターの表情を使います。終了後は、保持していた固定表情・パーツの選択、または現在のジェスチャーへ戻ります。", MessageType.None);
            MotionIntegration.Initialize(set, Descriptor);
            DrawMmdIntegration(set);
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("AFKの表情", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("元アバターのAFKモーションを使います。ベース顔を反映して崩れる部分は、AFKの表情を編集して調整できます。", EditorStyles.wordWrappedMiniLabel);
            DrawAfk();
            var afkRules = set.motionRules.Where(IsAfkRule).ToList();
            EditorGUILayout.LabelField(afkRules.Any(r => r.enabled && r.pauseExpressions)
                ? "AFKの優先条件が設定されています。条件の詳細は下で確認できます。"
                : "現在、AFK中に通常・固定表情を停止する設定は無効です。", EditorStyles.wordWrappedMiniLabel);
            _showAfkDetails = EditorGUILayout.Foldout(_showAfkDetails, "詳細：AFKの優先条件・停止する機能", true);
            if (_showAfkDetails) DrawMotionRules(set, true);
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("その他モーションの表情", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("付属ダンスなどの再生中は、そのアニメーションの表情を優先します。終了後は固定表情・パーツの選択、または現在のジェスチャーへ戻ります。", EditorStyles.wordWrappedMiniLabel);
            DrawMotionRules(set, false);
            EditorGUILayout.LabelField("同時に複数の場面に該当するときは、どれかで停止指定されている制御を停止します。", EditorStyles.wordWrappedMiniLabel);
            foreach (var error in MotionIntegration.Validate(set, Descriptor, set.motionRules)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void DrawMmdIntegration(ExpressionSet set)
        {
            EditorGUILayout.LabelField("MMD対応", EditorStyles.boldLabel);
            var unify = EditorGUILayout.ToggleLeft(new GUIContent(
                "ビルド時にFX全体のWrite Defaultsを一括ONにする",
                "初期値はON。ビルド用FXを一括ONにします。OFFにすると各ステートの設定を保持します。元アセットは変更しません。"), set.mmdUnifyFxWriteDefaults);
            if (unify != set.mmdUnifyFxWriteDefaults)
                Modify(set, "MMDのWrite Defaults設定", () => set.mmdUnifyFxWriteDefaults = unify);
            if (!set.mmdUnifyFxWriteDefaults)
                EditorGUILayout.HelpBox("Write Defaultsの一括変更を行わずにビルドします。MMD中の表情の切り替え・解除は、元FXの設定によって値が残る場合があります。", MessageType.Info);
        }

        private void DrawMotionRules(ExpressionSet set, bool afk)
        {
            if (_motionCandidates == null || _motionSourceRoot != AvatarRoot)
            { _motionCandidates = MotionIntegration.Candidates(Descriptor); _motionSourceRoot = AvatarRoot; }
            var parameters = MotionIntegration.Parameters(Descriptor);
            Undo.RecordObject(set, "既存モーションとの連携を変更");
            EditorGUI.BeginChangeCheck();
            var rules = set.motionRules.Where(r => IsAfkRule(r) == afk).ToList();
            if (rules.Count == 0)
                EditorGUILayout.LabelField(afk ? "未設定：AFK中に制御を譲るには、下のボタンで設定を追加します。" : "元の表情を優先したいモーションを追加してください。", EditorStyles.wordWrappedMiniLabel);
            foreach (var rule in rules)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    rule.enabled = EditorGUILayout.Toggle(rule.enabled, GUILayout.Width(18));
                    var open = EditorGUILayout.Foldout(_openMotionRules.Contains(rule.id), rule.name, true);
                    if (open) _openMotionRules.Add(rule.id); else _openMotionRules.Remove(rule.id);
                    using (new EditorGUI.DisabledScope(afk && rules.Count == 1))
                    if (GUILayout.Button(new GUIContent("削除", afk && rules.Count == 1 ? "AFKの優先を止める場合は、左のチェックを外してください。" : "この設定を削除"), GUILayout.Width(44)))
                    { set.motionRules.Remove(rule); GUI.changed = true; }
                }
                if (!_openMotionRules.Contains(rule.id))
                {
                    EditorGUILayout.LabelField(string.Join(" ＆ ", rule.conditions.Select(c => $"{c.parameter} {new[] { "オン", "オフ", "＝", "≠", "＞", "＜" }[(int)c.mode]} {((int)c.mode < 2 ? "" : c.value.ToString())}")), EditorStyles.wordWrappedMiniLabel);
                    continue;
                }
                rule.name = EditorGUILayout.TextField("モーション名", rule.name);
                EditorGUILayout.LabelField("元の表情を使う条件（すべて一致）", EditorStyles.boldLabel);
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
                if (GUILayout.Button("＋ 条件を追加")) { rule.conditions.Add(new MotionCondition { parameter = afk ? "AFK" : "" }); GUI.changed = true; }
                EditorGUILayout.LabelField("その間、停止するエディタの機能", EditorStyles.boldLabel);
                rule.pauseExpressions = EditorGUILayout.ToggleLeft("通常ジェスチャー・固定表情", rule.pauseExpressions);
                rule.pauseBlink = EditorGUILayout.ToggleLeft("まばたき", rule.pauseBlink);
                rule.pauseMouthCancel = EditorGUILayout.ToggleLeft("口変形キャンセル", rule.pauseMouthCancel);
                rule.pauseParts = EditorGUILayout.ToggleLeft("重ねるパーツ", rule.pauseParts);
                var details = EditorGUILayout.Foldout(_openMotionDetails.Contains(rule.id), "詳細：元ステートの解析・ベース顔補正", true);
                if (details) _openMotionDetails.Add(rule.id); else _openMotionDetails.Remove(rule.id);
                if (details)
                {
                    EditorGUILayout.HelpBox("元ステートの選択は条件を調べるための補助です。選ぶだけでは再生中の自動判定になりません。", MessageType.None);
                    DrawMotionSource(rule);
                    using (new EditorGUI.DisabledScope(rule.sourceClips.Count == 0))
                        rule.applyBaseFace = EditorGUILayout.ToggleLeft("参照元クリップにベース顔の補正を反映する", rule.applyBaseFace);
                    EditorGUILayout.LabelField(rule.applyBaseFace ? "補正はビルド用クリップに適用します。元アセットは変更しません。" : "参照元の表情をそのまま使います。", EditorStyles.wordWrappedMiniLabel);
                }
            }
            if (GUILayout.Button(afk ? "＋ AFKの優先条件を追加" : "＋ 元の表情を優先するモーションを追加"))
            {
                var rule = afk
                    ? new MotionIntegrationRule { name = "AFK", conditions = new List<MotionCondition> { new MotionCondition() }, sourceClips = FxAnalysis.AfkClips.ToList(), applyBaseFace = set.applyBaseFaceToAfk && FxAnalysis.AfkClips.Count > 0 }
                    : new MotionIntegrationRule();
                set.motionRules.Add(rule); _openMotionRules.Add(rule.id); GUI.changed = true;
            }
            if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(set); _faceDefaults = null; InvalidateDetailPreview(); }
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
