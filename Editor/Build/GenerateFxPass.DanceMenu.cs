using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class GenerateFxPass
    {
        // メニューONかつInStationの間、表情を譲る。FX全体の停止は予備方式だけ。
        // FXはweight 0でも状態を評価するため、ステーション退出・メニューOFFで復帰できる。
        private static void BuildDanceMenuControl(VirtualAnimatorController fx, CloneContext context, bool stopEntireFx)
        {
            EnsureParameter(fx, BuildPlan.DanceEnabledParameter, AnimatorControllerParameterType.Bool);
            EnsureParameter(fx, BuildPlan.DanceActiveParameter, AnimatorControllerParameterType.Bool);
            EnsureParameter(fx, "InStation", AnimatorControllerParameterType.Bool);
            var layer = VirtualLayer.Create(context, LayerPrefix + "Dance menu control");
            layer.DefaultWeight = 0;
            var sm = layer.StateMachine;
            var normal = sm.AddState("Normal", VirtualClip.Create("Dance normal"), Vector3.zero);
            var dance = sm.AddState("MMD station", VirtualClip.Create("Dance station"), new Vector3(300, 0, 0));
            normal.WriteDefaultValues = dance.WriteDefaultValues = false;
            sm.DefaultState = normal;
            normal.Transitions = ImmutableList.Create(Transition(dance, 0,
                Condition(BuildPlan.DanceEnabledParameter, AnimatorConditionMode.If, 0),
                Condition("InStation", AnimatorConditionMode.If, 0)));
            dance.Transitions = ImmutableList.Create(
                Transition(normal, 0, Condition(BuildPlan.DanceEnabledParameter, AnimatorConditionMode.IfNot, 0)),
                Transition(normal, 0, Condition("InStation", AnimatorConditionMode.IfNot, 0)));
            foreach (var item in new[] { (state: normal, active: false), (state: dance, active: true) })
            {
                var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                driver.localOnly = false;
                driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    name = BuildPlan.DanceActiveParameter, type = VRC_AvatarParameterDriver.ChangeType.Set,
                    value = item.active ? 1 : 0,
                });
                var behaviours = ImmutableList.Create<StateMachineBehaviour>(driver);
                if (stopEntireFx)
                {
                    var control = ScriptableObject.CreateInstance<VRCPlayableLayerControl>();
                    control.layer = VRC_PlayableLayerControl.BlendableLayer.FX;
                    control.goalWeight = item.active ? 0 : 1;
                    control.blendDuration = 0;
                    behaviours = behaviours.Add(control);
                }
                if (item.active)
                {
                    var tracking = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
                    tracking.trackingEyes = tracking.trackingMouth = VRC_AnimatorTrackingControl.TrackingType.Animation;
                    behaviours = behaviours.Add(tracking);
                }
                item.state.Behaviours = behaviours;
            }
            fx.Layers = fx.Layers.Append(layer).ToList();
        }
    }
}
