using Basin.Capabilities;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

/// <summary>
/// Asks a yes or no question with Deny and Allow buttons. Each line of the body text is drawn as one row.
/// </summary>
public sealed class ConfirmPromptView : SkiaPromptView
{
    private readonly string[] _lines;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmPromptView"/> class.
    /// </summary>
    /// <param name="theme">The theme to draw with.</param>
    /// <param name="prompt">The request that supplies the title and the body text.</param>
    public ConfirmPromptView(SkiaPromptTheme theme, in ConfirmPrompt prompt)
        : base(theme, prompt.Title, SkiaPortalPrompts.AppLine(prompt.AppId, prompt.DisplayName), prompt.IconPath)
    {
        _lines = prompt.Body.Split('\n');
        AddButton("Deny", PromptResponse.Denied, primary: false);
        AddButton("Allow", PromptResponse.Accepted, primary: true);
    }

    /// <summary>
    /// Gets the height of the dialog in logical pixels. It grows by 20 pixels for each body line.
    /// </summary>
    public override int Height => BodyTop + (_lines.Length * 20) + 16 + ButtonHeight + Padding + 12;

    /// <summary>
    /// Paints the body text, one line for each row.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="width">The surface width in logical pixels.</param>
    /// <param name="height">The surface height in logical pixels.</param>
    protected override void PaintBody(SKCanvas canvas, int width, int height)
    {
        var y = BodyTop + 14;
        foreach (var line in _lines)
        {
            Theme.DrawText(canvas, line, Theme.BodyFont, Padding, y, Theme.Foreground, width - 2 * Padding);
            y += 20;
        }
    }
}
