using System;
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
        public override string DisplayName => "MMD対応のWrite Defaultsを確認";
        protected override void Execute(BuildContext context)
        {
            var settings = context.GetState<MmdBuildState>();
            if (!settings.PreserveGimmicks) return;
            var controllers = context.Extension<AnimatorServicesContext>().ControllerContext.Controllers;
            if (!controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return;
            var off = fx.Layers.SelectMany(l => l.AllReachableNodes().OfType<VirtualState>()
                .Where(s => !s.WriteDefaultValues).Select(s => (layer: l.Name, state: s))).ToList();
            if (settings.UnifyWriteDefaults)
            {
                foreach (var item in off) item.state.WriteDefaultValues = true;
            }
            else if (off.Count > 0)
            {
                throw new InvalidOperationException("MMDの衣装維持方式にはFXのWrite Defaults ON統一が必要です。「モーション連携」でWD統一を有効にするか、予備方式を選んでください。WD OFF：\n" +
                    string.Join("\n", off.Take(20).Select(s => s.layer + " / " + s.state.Name)));
            }
        }
    }
}
