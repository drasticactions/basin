using Basin.WindowManager;
using SkiaSharp;

namespace DeskbarWm;

internal interface IApplet
{
    string Name { get; }

    string RenderState { get; }

    int PreferredHeight { get; }

    int MeasureWidth(SKFont font, int trayHeight);

    void Draw(SKCanvas canvas, SKPaint paint, SKFont font, Rect rect);
}
