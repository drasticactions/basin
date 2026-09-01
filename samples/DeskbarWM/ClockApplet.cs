using Basin.WindowManager;
using SkiaSharp;

namespace DeskbarWm;

internal sealed class ClockApplet(Config config) : IApplet
{
    private Config _config = config;

    public string Name => "clock";

    public string RenderState => Text;

    public int PreferredHeight => 19;

    public void Reconfigure(Config config) => _config = config;

    public string Text
    {
        get
        {
            var now = DateTime.Now;
            var text = string.Empty;
            if (_config.ClockShowDayOfWeek)
            {
                text += now.ToString("ddd ");
            }

            text += now.ToString(_config.ClockShowSeconds ? "HH:mm:ss" : "HH:mm");
            if (_config.ClockShowTimeZone)
            {
                var offset = TimeZoneInfo.Local.GetUtcOffset(now);
                text += $" {(offset < TimeSpan.Zero ? "-" : "+")}{offset:hh\\:mm}";
            }

            return text;
        }
    }

    public int MeasureWidth(SKFont font, int trayHeight) =>
        (int)MathF.Ceiling(font.MeasureText(Text)) + 10;

    public void Draw(SKCanvas canvas, SKPaint paint, SKFont font, Rect rect)
    {
        var metrics = font.Metrics;
        var baseline = rect.Y + ((rect.Height - metrics.Ascent - metrics.Descent) / 2f);
        paint.Color = SKColors.Black;
        paint.IsAntialias = true;
        canvas.DrawText(Text, rect.X + 5, baseline, SKTextAlign.Left, font, paint);
        paint.IsAntialias = false;
    }
}
