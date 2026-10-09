using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class GenerateFxPass
    {
        internal static AnimatorCondition MotionTest(MotionCondition c)
        {
            var mode = c.mode == MotionConditionMode.IsTrue ? AnimatorConditionMode.If : c.mode == MotionConditionMode.IsFalse ? AnimatorConditionMode.IfNot :
                c.mode == MotionConditionMode.Equals ? AnimatorConditionMode.Equals : c.mode == MotionConditionMode.NotEqual ? AnimatorConditionMode.NotEqual :
                c.mode == MotionConditionMode.Greater ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less;
            return Condition(c.parameter, mode, c.value);
        }
        internal static AnimatorCondition InverseMotionTest(MotionCondition c, AnimatorControllerParameterType type)
        {
            var test = MotionTest(c);
            switch (test.mode)
            {
                case AnimatorConditionMode.If: test.mode = AnimatorConditionMode.IfNot; break;
                case AnimatorConditionMode.IfNot: test.mode = AnimatorConditionMode.If; break;
                case AnimatorConditionMode.Equals: test.mode = AnimatorConditionMode.NotEqual; break;
                case AnimatorConditionMode.NotEqual: test.mode = AnimatorConditionMode.Equals; break;
                case AnimatorConditionMode.Greater:
                    test.mode = AnimatorConditionMode.Less;
                    test.threshold = type == AnimatorControllerParameterType.Int ? c.value + 1 : AdjacentFloat(c.value, true); break;
                case AnimatorConditionMode.Less:
                    test.mode = AnimatorConditionMode.Greater;
                    test.threshold = type == AnimatorControllerParameterType.Int ? c.value - 1 : AdjacentFloat(c.value, false); break;
            }
            return test;
        }
        private static float AdjacentFloat(float value, bool up)
        {
            if (value == 0) return up ? float.Epsilon : -float.Epsilon;
            var bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            bits += (value > 0) == up ? 1 : -1;
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }
        private static void BuildMotionIntegration(VirtualAnimatorController fx, CloneContext context, FaceControlPlan face,
            VirtualLayer expression, VirtualLayer blink, List<VirtualLayer> mouth, List<VirtualLayer> parts)
        {
            var evaluation = new List<VirtualLayer>();
            var requests = new Dictionary<VirtualLayer, List<string>>();
            foreach (var target in new[] { expression, blink }.Concat(mouth).Concat(parts).Where(l => l != null))
                requests[target] = new List<string> { BuildPlan.DanceActiveParameter };
            if (blink != null) requests[blink].Add(BuildPlan.BlinkOffParameter);
            for (var i = 0; i < face.MotionRules.Count; i++)
            {
                var rule = face.MotionRules[i];
                var parameter = BuildPlan.Prefix + "MotionRule/" + i;
                EnsureParameter(fx, parameter, AnimatorControllerParameterType.Bool);
                foreach (var c in rule.conditions)
                {
                    if (fx.Parameters.ContainsKey(c.parameter)) continue;
                    if (face.MotionParameterDefaults.TryGetValue(c.parameter, out var original))
                        fx.SetParameter(c.parameter, new AnimatorControllerParameter { name = c.parameter, type = original.type, defaultBool = original.defaultBool, defaultInt = original.defaultInt, defaultFloat = original.defaultFloat });
                    else EnsureParameter(fx, c.parameter, face.MotionParameters[c.parameter]);
                }
                var layer = VirtualLayer.Create(context, LayerPrefix + "Motion rule " + rule.name);
                var sm = layer.StateMachine;
                var off = sm.AddState("Inactive", VirtualClip.Create("Motion inactive"), Vector3.zero);
                var on = sm.AddState("Active", VirtualClip.Create("Motion active"), new Vector3(300, 0, 0));
                off.WriteDefaultValues = on.WriteDefaultValues = false; sm.DefaultState = off;
                off.Transitions = ImmutableList.Create(Transition(on, 0, rule.conditions.Select(MotionTest).ToArray()));
                on.Transitions = rule.conditions.Select(c => Transition(off, 0, InverseMotionTest(c, face.MotionParameters[c.parameter]))).ToImmutableList();
                foreach (var item in new[] { (state: off, value: 0f), (state: on, value: 1f) })
                {
                    var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                    driver.localOnly = false;
                    driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter { name = parameter, type = VRC_AvatarParameterDriver.ChangeType.Set, value = item.value });
                    item.state.Behaviours = ImmutableList.Create<StateMachineBehaviour>(driver);
                }
                evaluation.Add(layer);
                var targets = new List<VirtualLayer>();
                if (rule.pauseExpressions) targets.Add(expression);
                if (rule.pauseBlink && blink != null) targets.Add(blink);
                if (rule.pauseMouthCancel) targets.AddRange(mouth);
                if (rule.pauseParts) targets.AddRange(parts);
                foreach (var target in targets)
                {
                    if (!requests.ContainsKey(target)) requests[target] = new List<string>();
                    requests[target].Add(parameter);
                }
            }
            var gates = new List<VirtualLayer>();
            foreach (var request in requests)
            {
                var target = request.Key;
                var resumeConditions = request.Value.Select(p => Condition(p, AnimatorConditionMode.IfNot, 0)).ToArray();
                foreach (var transition in target.AllReachableNodes().OfType<VirtualStateTransition>())
                    transition.Conditions = transition.Conditions.AddRange(resumeConditions);
                var sm = target.StateMachine;
                var resume = sm.DefaultState;
                var suspended = sm.AddState("Motion suspend", VirtualClip.Create("Motion suspend"), new Vector3(0, -100, 0));
                suspended.WriteDefaultValues = false;
                suspended.Transitions = ImmutableList.Create(Transition(resume, 0, resumeConditions));
                sm.AnyStateTransitions = request.Value.Select(p => Transition(suspended, 0, Condition(p, AnimatorConditionMode.If, 0))).Concat(sm.AnyStateTransitions).ToImmutableList();

                var gate = VirtualLayer.Create(context, LayerPrefix + "Motion gate " + target.Name);
                var active = gate.StateMachine.AddState("Normal", VirtualClip.Create("Normal"), Vector3.zero);
                var yielded = gate.StateMachine.AddState("Yield", VirtualClip.Create("Yield"), new Vector3(300, 0, 0));
                active.WriteDefaultValues = yielded.WriteDefaultValues = false; gate.StateMachine.DefaultState = active;
                active.Transitions = request.Value.Select(p => Transition(yielded, 0, Condition(p, AnimatorConditionMode.If, 0))).ToImmutableList();
                yielded.Transitions = ImmutableList.Create(Transition(active, 0, resumeConditions));
                foreach (var item in new[] { (state: active, weight: 1f), (state: yielded, weight: 0f) })
                {
                    var control = ScriptableObject.CreateInstance<VRCAnimatorLayerControl>();
                    control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
                    control.layer = target.VirtualLayerIndex; control.goalWeight = item.weight; control.blendDuration = 0;
                    item.state.Behaviours = ImmutableList.Create<StateMachineBehaviour>(control);
                }
                gates.Add(gate);
            }
            // The mode-memory / Emote Release layer is intentionally never restarted.
            fx.Layers = evaluation.Concat(fx.Layers).Concat(gates).ToList();
        }
        private static void ApplyMotionCorrections(IEnumerable<VirtualAnimatorController> controllers, FaceControlPlan face, FaceVariant variant, GameObject root)
        {
            if (variant == null) return;
            var sources = new HashSet<AnimationClip>(face.MotionRules.Where(r => r.applyBaseFace).SelectMany(r => r.sourceClips).Where(c => c != null));
            if (sources.Count == 0) return;
            var done = new HashSet<VirtualClip>();
            foreach (var clip in controllers.Where(c => c != null).SelectMany(c => c.Layers).SelectMany(l => l.AllReachableNodes().OfType<VirtualState>()).SelectMany(s => ClipsOf(s.Motion)))
            {
                if (!done.Add(clip) || !sources.Contains(OriginalObjectProperty?.GetValue(clip) as AnimationClip)) continue;
                BaseFaceProcessor.Apply(variant, FaceVariant.AfkId, false, root, clip.GetFloatCurve, clip.SetFloatCurve, includeFaceValues: false);
                BaseFaceProcessor.ApplyAfkCurves(AfkCurvesOf(variant, clip), root, clip.SetFloatCurve);
            }
        }
    }
}
