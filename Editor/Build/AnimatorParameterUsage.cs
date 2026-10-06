using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// アニメーターのレイヤーが参照しているパラメータ名を集める。
    /// </summary>
    internal static class AnimatorParameterUsage
    {
        public static HashSet<string> Collect(IEnumerable<AnimatorControllerLayer> layers)
        {
            var result = new HashSet<string>();
            foreach (var layer in layers) Collect(layer.stateMachine, result, new HashSet<AnimatorStateMachine>());
            return result;
        }

        /// <summary>
        /// アバターの全Playable Layerのうち、除外したレイヤー以外が参照しているパラメータ名。
        /// </summary>
        public static HashSet<string> CollectFromAvatar(VRCAvatarDescriptor descriptor, AnimatorController fx,
            ICollection<string> excludedFxLayers)
        {
            var layers = new List<AnimatorControllerLayer>();
            var controllers = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                .Where(l => !l.isDefault)
                .Select(l => l.animatorController as AnimatorController)
                .Where(c => c != null)
                .Distinct();

            foreach (var controller in controllers)
            {
                layers.AddRange(controller == fx
                    ? controller.layers.Where(l => !excludedFxLayers.Contains(l.name))
                    : controller.layers);
            }

            return Collect(layers);
        }

        private static void Collect(AnimatorStateMachine stateMachine, HashSet<string> result,
            HashSet<AnimatorStateMachine> visited)
        {
            if (stateMachine == null || !visited.Add(stateMachine)) return;

            AddConditions(stateMachine.anyStateTransitions, result);
            AddConditions(stateMachine.entryTransitions, result);
            AddBehaviours(stateMachine.behaviours, result);

            foreach (var child in stateMachine.states)
            {
                var state = child.state;
                AddConditions(state.transitions, result);
                AddBehaviours(state.behaviours, result);
                AddMotion(state.motion, result);
                if (state.timeParameterActive) result.Add(state.timeParameter);
                if (state.speedParameterActive) result.Add(state.speedParameter);
                if (state.mirrorParameterActive) result.Add(state.mirrorParameter);
                if (state.cycleOffsetParameterActive) result.Add(state.cycleOffsetParameter);
            }

            foreach (var child in stateMachine.stateMachines)
            {
                AddConditions(stateMachine.GetStateMachineTransitions(child.stateMachine), result);
                Collect(child.stateMachine, result, visited);
            }
        }

        private static void AddConditions(IEnumerable<AnimatorTransitionBase> transitions, HashSet<string> result)
        {
            foreach (var transition in transitions)
            {
                if (transition == null) continue;
                foreach (var condition in transition.conditions) result.Add(condition.parameter);
            }
        }

        private static void AddMotion(Motion motion, HashSet<string> result)
        {
            if (!(motion is BlendTree tree)) return;
            result.Add(tree.blendParameter);
            if (tree.blendType != BlendTreeType.Simple1D) result.Add(tree.blendParameterY);
            foreach (var child in tree.children)
            {
                if (tree.blendType == BlendTreeType.Direct) result.Add(child.directBlendParameter);
                AddMotion(child.motion, result);
            }
        }

        private static void AddBehaviours(IEnumerable<StateMachineBehaviour> behaviours, HashSet<string> result)
        {
            foreach (var behaviour in behaviours)
            {
                if (!(behaviour is VRCAvatarParameterDriver driver)) continue;
                foreach (var p in driver.parameters)
                {
                    result.Add(p.name);
                    if (!string.IsNullOrEmpty(p.source)) result.Add(p.source);
                }
            }
        }
    }
}
