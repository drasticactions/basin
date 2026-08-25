using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class ColorBlindnessStage : IPostStage
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IPixelShader? _shader;
    private ColorBlindnessMode _mode = ColorBlindnessMode.Protanopia;
    private double _intensity = 1.0;

    public ColorBlindnessStage(IPixelShader? shader)
    {
        _shader = shader;
        Push();
    }

    public bool IsSupported => _shader is not null;

    public ColorBlindnessMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            Push();
        }
    }

    public double Intensity
    {
        get => _intensity;
        set
        {
            _intensity = Math.Clamp(value, 0, 1);
            Push();
        }
    }

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

    private void Push() =>
        _shader?.SetUniforms([(float)(int)_mode, (float)_intensity]);
}
