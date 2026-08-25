using Basin.Diagnostics;
using Basin.Scene;
using Pixman;

namespace Basin.Effects;

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

    public float Alpha { get; set; } = 0.2f;

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

        var colour = Palette[_next];
        _next = (_next + 1) % Palette.Length;
        var alpha = Math.Clamp(Alpha, 0f, 1f);
        var tint = new RenderColor(colour.R * alpha, colour.G * alpha, colour.B * alpha, alpha);
        foreach (var rect in RegionRects.Of(damage))
        {
            pass.AddRect(tint, new Box(rect.X1, rect.Y1, rect.X2 - rect.X1, rect.Y2 - rect.Y1));
        }
    }
}
