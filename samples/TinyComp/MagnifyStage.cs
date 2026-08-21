using Basin;
using Basin.Effects;
using Basin.Scene;

namespace TinyComp;

internal sealed class MagnifyStage : IPostStage
{
    public double CenterX { get; set; }

    public double CenterY { get; set; }

    public void Render(IRenderPass pass, ITexture frame, in PostContext context)
    {
        pass.AddTexture(frame, new TextureRenderOptions
        {
            DstBox = new Box(0, 0, context.Width, context.Height),
            Transform = RenderTransform.Multiply(
                RenderTransform.Translation(CenterX, CenterY),
                RenderTransform.Multiply(RenderTransform.Scale(2, 2), RenderTransform.Translation(-CenterX, -CenterY))),
        });
    }
}
