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
            // メニューとパラメータはMAのコンポーネントとして作るので、MAが処理する前に置いておく。
            InPhase(BuildPhase.Generating)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run(GenerateMenuPass.Instance);

            // MAで合成された付属FXも含めて、選択した表情レイヤーを置き換える。
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq =>
                {
                    seq.Run(GenerateFxPass.Instance);
                });
        }
    }
}
