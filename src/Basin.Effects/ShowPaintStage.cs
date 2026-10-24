using Basin.Diagnostics;
using Basin.Scene;
using Pixman;

namespace Basin.Effects;

/// <summary>
/// Draws KWin's show-paint overlay. It tints the damaged region of each frame with the next color of a six-color cycle.
/// </summary>
public sealed class ShowPaintStage : IPostStage
{
    private static readonly RenderColor[] Palette =
    [
        new(0f, 0f, 1f, 1f),
        new(0f, 1f, 0f, 1f),
        new(0f, 1f, 1f, 1f),
        new(1f, 0f, 0f, 1f),
        new(1f, 0f, 1f, 1f),
        new(1f, 1f, 0f, 1f),
    ];

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private int _next;

    /// <summary>
    /// Gets or sets the opacity of the tint, clamped between 0 and 1 when drawn. The default is 0.2.
    /// </summary>
    public float Alpha { get; set; } = 0.2f;

    /// <summary>
    /// Reports that a box stays where it is, because the stage only draws over the frame.
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
        pass.AddTexture(frame, new TextureRenderOptions { DstBox = new Box(0, 0, context.Width, context.Height) });
        if (context.Damage is not { IsEmpty: false } damage)
        {
            return;
        }

        var color = Palette[_next];
        _next = (_next + 1) % Palette.Length;
        var alpha = Math.Clamp(Alpha, 0f, 1f);
        var tint = new RenderColor(color.R * alpha, color.G * alpha, color.B * alpha, alpha);
        foreach (var rect in RegionRects.Of(damage))
        {
            pass.AddRect(tint, new Box(rect.X1, rect.Y1, rect.X2 - rect.X1, rect.Y2 - rect.Y1));
        }
    }
}
