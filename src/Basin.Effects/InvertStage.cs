using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Applies KWin's color inversion to the whole output as a post stage.
/// </summary>
public sealed class InvertStage : IPostStage
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IPixelShader? _shader;

    /// <summary>
    /// Initializes a new instance of the <see cref="InvertStage"/> class.
    /// </summary>
    /// <param name="shader">The compiled <see cref="InvertShader"/>, or <see langword="null"/> when the renderer cannot compile it. Without it, the frame draws unchanged.</param>
    public InvertStage(IPixelShader? shader) => _shader = shader;

    /// <summary>
    /// Gets a value indicating whether the stage has a shader and so inverts the frame.
    /// </summary>
    public bool IsSupported => _shader is not null;

    /// <summary>
    /// Reports that a box stays where it is, because the stage changes only colors.
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
}
