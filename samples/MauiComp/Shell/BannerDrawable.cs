using Microsoft.Maui.Graphics;

namespace MauiComp.Shell;

public sealed class BannerDrawable : IDrawable
{
    public string Text { get; set; } = ".NET MAUI";

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();
        canvas.Translate(0, dirtyRect.Height);
        canvas.Rotate(-90);
        canvas.FontColor = Colors.White;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = 14;
        canvas.DrawString(
            Text,
            8,
            0,
            dirtyRect.Height - 8,
            dirtyRect.Width,
            HorizontalAlignment.Left,
            VerticalAlignment.Center);
        canvas.RestoreState();
    }
}
