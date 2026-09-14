using Basin.Capabilities;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

public sealed class ConfirmPromptView : SkiaPromptView
{
    private readonly string[] _lines;

    public ConfirmPromptView(SkiaPromptTheme theme, in ConfirmPrompt prompt)
        : base(theme, prompt.Title, SkiaPortalPrompts.AppLine(prompt.AppId, prompt.DisplayName), prompt.IconPath)
    {
        _lines = prompt.Body.Split('\n');
        AddButton("Deny", PromptResponse.Denied, primary: false);
        AddButton("Allow", PromptResponse.Accepted, primary: true);
    }

    public override int Height => BodyTop + (_lines.Length * 20) + 16 + ButtonHeight + Padding + 12;

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
