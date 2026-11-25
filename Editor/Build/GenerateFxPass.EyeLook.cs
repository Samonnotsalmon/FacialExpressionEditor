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
        private static void BuildManualEyeLook(VirtualAnimatorController fx, IEnumerable<VirtualAnimatorController> controllers,
            CloneContext context, ExpressionSet set)
        {
            ManualEyeLook.Validate(set);
            EnsureParameter(fx, ManualEyeLook.Enabled, AnimatorControllerParameterType.Bool);
            EnsureParameter(fx, ManualEyeLook.X, AnimatorControllerParameterType.Float);
            EnsureParameter(fx, ManualEyeLook.Y, AnimatorControllerParameterType.Float);
            EnsureParameter(fx, ManualEyeLook.RequestedTracking, AnimatorControllerParameterType.Bool, true);

            // 元の視線指示を記憶し、手動操作中の表情切替でもTrackingに戻されないようにする。
            // 複製されたビルド用Behaviourのみ置換し、口などの指示は保持する。
            foreach (var controller in controllers.Where(c => c != null).Distinct())
            {
                EnsureParameter(controller, ManualEyeLook.RequestedTracking, AnimatorControllerParameterType.Bool, true);
                foreach (var state in controller.Layers.SelectMany(l => l.AllReachableNodes().OfType<VirtualState>()).Distinct())
                {
                    var behaviours = new List<StateMachineBehaviour>();
                    foreach (var behaviour in state.Behaviours)
                    {
                        if (!(behaviour is VRCAnimatorTrackingControl tracking) || tracking.trackingEyes == VRC_AnimatorTrackingControl.TrackingType.NoChange)
                        { behaviours.Add(behaviour); continue; }
                        var copy = Object.Instantiate(tracking);
                        copy.trackingEyes = VRC_AnimatorTrackingControl.TrackingType.NoChange;
                        behaviours.Add(copy);
                        var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                        driver.localOnly = false;
                        driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                        {
                            name = ManualEyeLook.RequestedTracking, type = VRC_AvatarParameterDriver.ChangeType.Set,
                            value = tracking.trackingEyes == VRC_AnimatorTrackingControl.TrackingType.Tracking ? 1 : 0,
                        });
                        behaviours.Add(driver);
                    }
                    state.Behaviours = behaviours.ToImmutableList();
                }
            }

            var layer = VirtualLayer.Create(context, LayerPrefix + "Manual eye look");
            layer.DefaultWeight = 0;
            var tree = VirtualBlendTree.Create("Manual eye look");
            tree.BlendType = BlendTreeType.SimpleDirectional2D;
            tree.BlendParameter = ManualEyeLook.X; tree.BlendParameterY = ManualEyeLook.Y;
            tree.Children = set.eyeLookPoses.Select((source, index) =>
            {
                var pose = VirtualClip.Create("Eye look " + ManualEyeLook.Labels[index]);
                foreach (var binding in AnimationUtility.GetCurveBindings(set.eyeLookPoses[0]))
                {
                    var value = (AnimationUtility.GetEditorCurve(source, binding) ?? AnimationUtility.GetEditorCurve(set.eyeLookPoses[0], binding)).Evaluate(0);
                    pose.SetFloatCurve(binding, AnimationCurve.Constant(0, 1, value));
                }
                return new VirtualBlendTree.VirtualChildMotion { Motion = pose, Position = ManualEyeLook.Positions[index], TimeScale = 1 };
            }).ToImmutableList();
            var sm = layer.StateMachine;
            var tracked = sm.AddState("Original tracking", VirtualClip.Create("Eye look off"), Vector3.zero);
            var animated = sm.AddState("Original animation", VirtualClip.Create("Eye look off"), new Vector3(0, 100));
            var manual = sm.AddState("Manual", tree, new Vector3(300, 0));
            sm.DefaultState = tracked;
            foreach (var state in new[] { tracked, animated, manual })
            {
                state.WriteDefaultValues = false;
                var control = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
                control.trackingEyes = state == tracked ? VRC_AnimatorTrackingControl.TrackingType.Tracking : VRC_AnimatorTrackingControl.TrackingType.Animation;
                state.Behaviours = ImmutableList.Create<StateMachineBehaviour>(control);
                SetPartLayerWeight(state, layer, state == manual ? 1 : 0);
            }
            tracked.Transitions = ImmutableList.Create(
                Transition(manual, 0, Condition(ManualEyeLook.Enabled, AnimatorConditionMode.If, 0)),
                Transition(animated, 0, Condition(ManualEyeLook.Enabled, AnimatorConditionMode.IfNot, 0), Condition(ManualEyeLook.RequestedTracking, AnimatorConditionMode.IfNot, 0)));
            animated.Transitions = ImmutableList.Create(
                Transition(manual, 0, Condition(ManualEyeLook.Enabled, AnimatorConditionMode.If, 0)),
                Transition(tracked, 0, Condition(ManualEyeLook.Enabled, AnimatorConditionMode.IfNot, 0), Condition(ManualEyeLook.RequestedTracking, AnimatorConditionMode.If, 0)));
            manual.Transitions = ImmutableList.Create(
                Transition(tracked, 0, Condition(ManualEyeLook.Enabled, AnimatorConditionMode.IfNot, 0), Condition(ManualEyeLook.RequestedTracking, AnimatorConditionMode.If, 0)),
                Transition(animated, 0, Condition(ManualEyeLook.Enabled, AnimatorConditionMode.IfNot, 0), Condition(ManualEyeLook.RequestedTracking, AnimatorConditionMode.IfNot, 0)));
            fx.Layers = fx.Layers.Append(layer).ToList();
        }
    }
}
