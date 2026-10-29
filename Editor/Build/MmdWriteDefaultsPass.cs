using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    internal sealed class MmdBuildState
    {
        public bool PreserveGimmicks;
        public bool UnifyWriteDefaults;
    }

    internal sealed class MmdWriteDefaultsPass : Pass<MmdWriteDefaultsPass>
    {
        public override string DisplayName => "MMD対応のWrite Defaults設定を適用";
        protected override void Execute(BuildContext context)
        {
            var settings = context.GetState<MmdBuildState>();
            // OFF means preserve each state's WD setting, not reject the avatar.
            if (!settings.PreserveGimmicks || !settings.UnifyWriteDefaults) return;
            var controllers = context.Extension<AnimatorServicesContext>().ControllerContext.Controllers;
            if (!controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return;
            var off = fx.Layers.SelectMany(l => l.AllReachableNodes().OfType<VirtualState>()
                .Where(s => !s.WriteDefaultValues).Select(s => (layer: l.Name, state: s))).ToList();
            foreach (var item in off) item.state.WriteDefaultValues = true;
        }
    }
}
