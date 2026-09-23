using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.UI.Quill;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace TinyComp;

internal sealed class QuillDemo : IDisposable
{
    private const float SurfaceWidth = 480f;
    private const float SurfaceHeight = 360f;
    private const float WindowHeight = 320f;
    private const float TitleHeight = 32f;
    private const float PanelHeight = SurfaceHeight - WindowHeight;
    private const string Title = "Prowl.Quill on basin";

    private readonly UISurfaceNode _node;
    private readonly IEventSource _clockTimer;
    private readonly BasinLogger _log;
    private FontFile? _font;
    private bool _fontResolved;
    private string _clock = string.Empty;
    private bool _disposed;

    internal QuillDemo(SceneTree parent, IUIHost host, ICompositorEventLoop loop, double scale, BasinLogger log)
    {
        _log = log;
        _node = new UISurfaceNode(parent, host) { Target = UITargetKind.Dmabuf, InputEnabled = false };
        _node.SetPosition(560, 60);
        _node.Faulted += error => _log.Error($"quill demo: {error.Message}");
        _node.Configure((int)SurfaceWidth, (int)SurfaceHeight, scale);
        _clockTimer = loop.AddTimer(OnClockTick);
        OnClockTick();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clockTimer.Remove();
        _node.Dispose();
    }

    private void OnClockTick()
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTime.Now;
        _clock = now.ToString("HH:mm:ss");
        Paint();
        _clockTimer.UpdateTimer(Math.Max(1, 1000 - now.Millisecond));
    }

    private void Paint()
    {
        if (_node.IsFaulted || _node.Surface is not IQuillUISurface surface)
        {
            return;
        }

        Canvas canvas;
        try
        {
            canvas = surface.BeginDraw();
        }
        catch (InvalidOperationException error)
        {
            _log.Error($"quill demo: {error.Message}");
            return;
        }

        if (!_fontResolved)
        {
            _fontResolved = true;
            _font = QuillFonts.Resolve(canvas);
            var face = _font is null ? "none" : _font.FamilyName;
            _log.Info($"quill demo: {QuillShaders.SourcePackage}, font {face}");
        }

        Draw(canvas);
        surface.EndDraw();
        _node.Publish();
    }

    private void Draw(Canvas canvas)
    {
        canvas.SetLinearBrush(0, 0, 0, TitleHeight, new Color32(0x4a, 0x50, 0x5c, 0xff), new Color32(0x26, 0x2a, 0x33, 0xff));
        canvas.BeginPath();
        canvas.RoundedRect(0, 0, SurfaceWidth, TitleHeight, 10, 10, 0, 0);
        canvas.Fill();
        canvas.ClearBrush();

        canvas.SetFillColor(new Color32(0x1b, 0x1d, 0x23, 0xff));
        canvas.BeginPath();
        canvas.Rect(0, TitleHeight, SurfaceWidth, WindowHeight - TitleHeight);
        canvas.Fill();

        canvas.SetStrokeColor(new Color32(0x70, 0x78, 0x86, 0xff));
        canvas.SetStrokeWidth(1);
        canvas.BeginPath();
        canvas.RoundedRect(0.5f, 0.5f, SurfaceWidth - 1f, WindowHeight - 1f, 10, 10, 0, 0);
        canvas.Stroke();

        canvas.CircleFilled(SurfaceWidth - 24f, TitleHeight / 2f, 6f, new Color32(0xff, 0x5f, 0x57, 0xff));
        canvas.CircleFilled(SurfaceWidth - 46f, TitleHeight / 2f, 6f, new Color32(0xfe, 0xbc, 0x2e, 0xff));
        canvas.CircleFilled(SurfaceWidth - 68f, TitleHeight / 2f, 6f, new Color32(0x28, 0xc8, 0x40, 0xff));

        canvas.SetFillColor(new Color32(0x0e, 0x10, 0x14, 0xf2));
        canvas.BeginPath();
        canvas.Rect(0, WindowHeight, SurfaceWidth, PanelHeight);
        canvas.Fill();

        if (_font is not { } font)
        {
            return;
        }

        canvas.DrawText(
            Title, 14f, TitleHeight / 2f, new Color32(0xf2, 0xf4, 0xf7, 0xff), 13f, font,
            0f, new Float2(0f, 0.5f));
        canvas.DrawText(
            _clock, SurfaceWidth - 14f, WindowHeight + (PanelHeight / 2f), new Color32(0xdc, 0xe2, 0xea, 0xff), 15f, font,
            0f, new Float2(1f, 0.5f));
    }
}
