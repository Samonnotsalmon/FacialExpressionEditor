using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    internal sealed class MenuBuildState { public bool Generated; }

    // MA merges Saved with OR, so legacy saved settings must be corrected after the merge.
    internal sealed class FinalizeMenuParametersPass : Pass<FinalizeMenuParametersPass>
    {
        public override string DisplayName => "瞬き・MMDの選択を非保存にする";
        protected override void Execute(BuildContext context)
        {
            if (!context.GetState<MenuBuildState>().Generated) return;
            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null || descriptor.expressionParameters == null) return;
            var parameters = Object.Instantiate(descriptor.expressionParameters);
            parameters.name = descriptor.expressionParameters.name;
            foreach (var parameter in parameters.parameters.Where(p =>
                p.name == BuildPlan.BlinkOffParameter || p.name == BuildPlan.DanceEnabledParameter))
            {
                parameter.saved = false;
                parameter.defaultValue = 0;
            }
            context.AssetSaver.SaveAsset(parameters);
            descriptor.expressionParameters = parameters;
        }
    }
}
