using Basin;
using Basin.Effects;
using Basin.Scene;

namespace TinyComp;

internal sealed class InvertStage : IPostStage, IDisposable
{
    private readonly IColorLut? _lut;

    public InvertStage(IRenderer renderer)
    {
        if (renderer.ColorTransform == ColorTransformCapability.Lut3D)
        {
            var data = new float[2 * 2 * 2 * 3];
            for (var b = 0; b < 2; b++)
            {
                for (var g = 0; g < 2; g++)
                {
                    for (var r = 0; r < 2; r++)
                    {
                        var index = (((b * 2) + g) * 2 + r) * 3;
                        data[index] = 1f - r;
                        data[index + 1] = 1f - g;
                        data[index + 2] = 1f - b;
                    }
                }
            }

            _lut = renderer.ImportLut(new ColorLut3D(2, data));
        }
    }

    public void Render(IRenderPass pass, ITexture frame, in PostContext context)
    {
        pass.AddTexture(frame, new TextureRenderOptions
        {
            DstBox = new Box(0, 0, context.Width, context.Height),
            Lut = _lut,
        });
    }

    public void Dispose() => _lut?.Dispose();
}
