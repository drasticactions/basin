using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class InvertStage : IPostStage
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IPixelShader? _shader;

    public InvertStage(IPixelShader? shader) => _shader = shader;

    public bool IsSupported => _shader is not null;

    public void Render(IRenderPass pass, ITexture frame, in PostContext context)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(frame);
        _thread.Assert();
        pass.AddTexture(frame, new TextureRenderOptions
        {
            DstBox = new Box(0, 0, context.Width, context.Height),
            Shader = _shader,
        });
    }
}
