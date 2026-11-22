using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using Samon.FacialExpressionEditor.Editor;

[assembly: ExportsPlugin(typeof(FacialExpressionPlugin))]

namespace Samon.FacialExpressionEditor.Editor
{
    public class FacialExpressionPlugin : Plugin<FacialExpressionPlugin>
    {
        public override string QualifiedName => "jp.samon.facial-expression-editor";
        public override string DisplayName => "NSL_Facial Expression Editor";

        protected override void Configure()
        {
            // Check before the standalone plugin can consume/remove its authoring component.
            InPhase(BuildPhase.Resolving).Run("リップシンク設定の重複を確認",
                context => LipSyncSwitchPass.CheckDuplicate(context.AvatarRootObject));
            // メニューとパラメータはMAのコンポーネントとして作るので、MAが処理する前に置いておく。
            var generating = InPhase(BuildPhase.Generating).BeforePlugin("nadena.dev.modular-avatar");
            generating.Run(LipSyncSwitchPass.Instance);
            generating.Run(GenerateMenuPass.Instance);

            // MAで合成された付属FXも含めて、選択した表情レイヤーを置き換える。
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq =>
                {
                    seq.Run(GenerateFxPass.Instance);
                });

            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run(FinalizeMenuParametersPass.Instance);

            // 他のTransformingパスが追加したFXも含め、ビルド用データで整合性を取る。
            InPhase(BuildPhase.Optimizing)
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq => seq.Run(MmdWriteDefaultsPass.Instance));
        }
    }
}
