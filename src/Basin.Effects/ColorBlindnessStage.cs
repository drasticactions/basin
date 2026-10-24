using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// A post stage that corrects the frame for a color vision defect. Without a shader it draws the frame unchanged.
/// </summary>
public sealed class ColorBlindnessStage : IPostStage
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IPixelShader? _shader;
    private ColorBlindnessMode _mode = ColorBlindnessMode.Protanopia;
    private double _intensity = 1.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorBlindnessStage"/> class.
    /// </summary>
    /// <param name="shader">The compiled color-blindness shader, or <see langword="null"/> when the renderer cannot compile it.</param>
    public ColorBlindnessStage(IPixelShader? shader)
    {
        _shader = shader;
        Push();
    }

    /// <summary>
    /// Gets a value indicating whether the stage has a shader and can change the frame.
    /// </summary>
    public bool IsSupported => _shader is not null;

    /// <summary>
    /// Gets or sets the defect that the stage corrects for. A change takes effect on the next frame.
    /// </summary>
    public ColorBlindnessMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            Push();
        }
    }

    /// <summary>
    /// Gets or sets how much of the correction applies, from 0 to 1. The setter clamps values outside that range.
    /// </summary>
    public double Intensity
    {
        get => _intensity;
        set
        {
            _intensity = Math.Clamp(value, 0, 1);
            Push();
        }
    }

    /// <summary>
    /// Leaves the box where it is, because the stage draws the frame at its own position. Unlike the default, it always returns <see langword="true"/>.
    /// </summary>
    /// <param name="scene">The box in the composited frame, in physical pixels.</param>
    /// <param name="context">The frame size, timing and damage.</param>
    /// <param name="output">The same box as <paramref name="scene"/>.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool TryMapBox(in Box scene, in PostContext context, out Box output)
    {
        output = scene;
        return true;
    }

    /// <inheritdoc/>
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
