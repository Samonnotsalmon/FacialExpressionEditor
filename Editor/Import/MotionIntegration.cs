using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using nadena.dev.modular_avatar.core;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class MotionIntegration
    {
        internal sealed class Candidate
        {
            public string playable, layer, state;
            public List<AnimationClip> clips;
            public List<AnimatorCondition[]> entries = new List<AnimatorCondition[]>();
            public List<AnimatorCondition[]> exits = new List<AnimatorCondition[]>();
            public bool timedExit;
            public List<string> parameterChanges = new List<string>();
            public string Label => $"{playable} / {layer} / {state}";
        }
        public static IEnumerable<(string playable, AnimatorController controller)> Controllers(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) yield break;
            foreach (var layer in descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers))
                if (!layer.isDefault && layer.animatorController is AnimatorController controller) yield return (layer.type.ToString(), controller);
            foreach (var merge in descriptor.GetComponentsInChildren<ModularAvatarMergeAnimator>(true))
                if (merge.animator is AnimatorController controller) yield return (merge.layerType.ToString(), controller);
        }
        public static Dictionary<string, AnimatorControllerParameterType> Parameters(VRCAvatarDescriptor descriptor)
        {
            var result = Controllers(descriptor).SelectMany(c => c.controller.parameters).GroupBy(p => p.name).ToDictionary(g => g.Key, g => g.First().type);
            result["AFK"] = AnimatorControllerParameterType.Bool;
            result["Seated"] = AnimatorControllerParameterType.Bool;
            result["InStation"] = AnimatorControllerParameterType.Bool;
            return result;
        }
        public static List<MotionIntegrationRule> Rules(ExpressionSet set, VRCAvatarDescriptor descriptor)
        {
            var rules = set.motionRules.ToList();
            if (!set.motionRulesConfigured && set.protectDance && Parameters(descriptor).TryGetValue(set.danceParameter ?? "", out var type) && type == AnimatorControllerParameterType.Int)
                rules.Add(new MotionIntegrationRule { id = "legacy_motion", name = "既存モーション（旧設定から移行）", conditions = new List<MotionCondition> {
                    new MotionCondition { parameter = set.danceParameter, mode = MotionConditionMode.NotEqual, value = 0 } } });
            return rules;
        }
        public static void Initialize(ExpressionSet set, VRCAvatarDescriptor descriptor)
        {
            if (set.motionRulesConfigured) return;
            Undo.RecordObject(set, "既存モーション連携を移行");
            set.motionRules = Rules(set, descriptor); set.motionRulesConfigured = true;
            EditorUtility.SetDirty(set);
        }
        public static IEnumerable<AnimationClip> Clips(Motion motion)
        {
            if (motion is AnimationClip clip) yield return clip;
            else if (motion is BlendTree tree) foreach (var c in tree.children.SelectMany(child => Clips(child.motion))) yield return c;
        }
        public static List<Candidate> Candidates(VRCAvatarDescriptor descriptor)
        {
            var result = new List<Candidate>();
            foreach (var source in Controllers(descriptor).Distinct())
            foreach (var layer in source.controller.layers)
            {
                var incoming = new Dictionary<AnimatorState, List<AnimatorCondition[]>>();
                void ReadTransitions(AnimatorStateMachine machine)
                {
                    foreach (var t in machine.anyStateTransitions.Cast<AnimatorTransitionBase>().Concat(machine.entryTransitions).Concat(machine.states.SelectMany(s => s.state.transitions)))
                        if (t.destinationState != null)
                        {
                            if (!incoming.ContainsKey(t.destinationState)) incoming[t.destinationState] = new List<AnimatorCondition[]>();
                            incoming[t.destinationState].Add(t.conditions);
                        }
                    foreach (var child in machine.stateMachines) ReadTransitions(child.stateMachine);
                }
                void Walk(AnimatorStateMachine machine, string path)
                {
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        var clips = Clips(state.motion).Distinct().ToList();
                        if (clips.Count == 0) continue;
                        result.Add(new Candidate { playable = source.playable, layer = layer.name, state = path + state.name, clips = clips,
                            entries = incoming.TryGetValue(state, out var entries) ? entries : new List<AnimatorCondition[]>(),
                            exits = state.transitions.Select(t => t.conditions).ToList(), timedExit = state.transitions.Any(t => t.hasExitTime),
                            parameterChanges = state.behaviours.OfType<VRCAvatarParameterDriver>().SelectMany(d => d.parameters).Select(p =>
                                p.type == VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set || p.type == VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add
                                    ? $"{p.name} : {p.type} ({p.value})" : $"{p.name} : {p.type}（詳細は元ステートのParameter Driver）").ToList() });
                    }
                    foreach (var child in machine.stateMachines) Walk(child.stateMachine, path + child.stateMachine.name + "/");
                }
                ReadTransitions(layer.stateMachine); Walk(layer.stateMachine, "");
            }
            return result;
        }
        public static MotionCondition FromAnimator(AnimatorCondition condition)
        {
            var mode = condition.mode == AnimatorConditionMode.If ? MotionConditionMode.IsTrue : condition.mode == AnimatorConditionMode.IfNot ? MotionConditionMode.IsFalse :
                condition.mode == AnimatorConditionMode.Equals ? MotionConditionMode.Equals : condition.mode == AnimatorConditionMode.NotEqual ? MotionConditionMode.NotEqual :
                condition.mode == AnimatorConditionMode.Greater ? MotionConditionMode.Greater : MotionConditionMode.Less;
            return new MotionCondition { parameter = condition.parameter, mode = mode, value = condition.threshold };
        }
        public static List<string> Validate(ExpressionSet set, VRCAvatarDescriptor descriptor, IEnumerable<MotionIntegrationRule> rules)
        {
            var errors = new List<string>(); var parameters = Parameters(descriptor);
            var controllers = Controllers(descriptor).ToList();
            var enabled = rules.Where(r => r.enabled).ToList();
            foreach (var rule in enabled)
            {
                if (rule.conditions.Count == 0) errors.Add(rule.name + "：有効条件を追加してください。");
                if (rule.applyBaseFace && !rule.sourceClips.Any(c => c != null)) errors.Add(rule.name + "：補正する元ステートを選んでください。");
                if (!string.IsNullOrEmpty(rule.sourceLayer) && !controllers.Any(c => c.playable == rule.sourcePlayable && c.controller.layers.Any(l => l.name == rule.sourceLayer)))
                    errors.Add(rule.name + "：参照元レイヤーが見つかりません。選び直してください。");
                if (rule.sourcePlayable == "FX" && (set.originalGestureLayers.Contains(rule.sourceLayer) || set.originalPartLayers.Contains(rule.sourceLayer) || set.originalLayerSettings.Any(s => s.layerName == rule.sourceLayer && s.mode != OriginalLayerMode.Keep)))
                    errors.Add(rule.name + "：連携元のレイヤーが置換・削除対象です。元レイヤーを残してください。");
                foreach (var c in rule.conditions)
                {
                    if (!parameters.TryGetValue(c.parameter ?? "", out var type)) { errors.Add(rule.name + "：パラメータが見つかりません：" + c.parameter); continue; }
                    if (controllers.SelectMany(s => s.controller.parameters).Where(p => p.name == c.parameter).Select(p => p.type).Distinct().Count() > 1)
                        errors.Add(rule.name + "：複数のコントローラーでパラメータの型が異なります：" + c.parameter);
                    var boolean = c.mode == MotionConditionMode.IsTrue || c.mode == MotionConditionMode.IsFalse;
                    if (type == AnimatorControllerParameterType.Trigger || boolean != (type == AnimatorControllerParameterType.Bool) ||
                        (type == AnimatorControllerParameterType.Float && (c.mode == MotionConditionMode.Equals || c.mode == MotionConditionMode.NotEqual)) ||
                        float.IsNaN(c.value) || float.IsInfinity(c.value) || (type == AnimatorControllerParameterType.Int && (c.value != Mathf.Round(c.value) || Mathf.Abs(c.value) > 16777214)))
                        errors.Add(rule.name + "：パラメータの型に合わない条件です：" + c.parameter);
                }
            }
            foreach (var group in enabled.SelectMany(r => r.sourceClips.Where(c => c != null).Select(c => (clip: c, apply: r.applyBaseFace))).GroupBy(x => x.clip))
                if (group.Select(x => x.apply).Distinct().Count() > 1) errors.Add(group.Key.name + "：同じクリップのベース補正指定がルール間で異なります。");
            return errors;
        }
    }
}
