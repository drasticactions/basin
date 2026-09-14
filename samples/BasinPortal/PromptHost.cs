using Basin;
using Basin.Diagnostics;
using Basin.Portal.Client;
using Basin.Portal.Prompts.Skia;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace BasinPortal;

internal sealed class PromptHost : ISkiaPromptHost, IDisposable
{
    private readonly ClientGlobals _globals;
    private readonly ZwlrLayerShellV1? _layerShell;
    private readonly ClientOutputs _outputs;
    private readonly ClientLoop _loop;
    private readonly BasinLogger _log;
    private readonly List<PromptSurface> _shown = [];

    public PromptHost(ClientGlobals globals, ZwlrLayerShellV1? layerShell, ClientOutputs outputs, ClientLoop loop, BasinLogger log)
    {
        _globals = globals;
        _layerShell = layerShell;
        _outputs = outputs;
        _loop = loop;
        _log = log;
    }

    public int Open => _shown.Count;

    public IOutput? OutputFor(string parentWindow) => First();

    public ISkiaPromptSurface? Show(SkiaPromptView view, IOutput output, bool cover)
    {
        if (_layerShell is not { } layerShell || _globals.Compositor is not { } compositor ||
            _globals.Shm is not { } shm || output is not ClientOutput portal)
        {
            _log.Warn($"no layer shell to show a portal prompt");
            return null;
        }

        var surface = new PromptSurface(this, compositor, shm, layerShell, portal, view, cover);
        _shown.Add(surface);
        _loop.RequestFlush();
        return surface;
    }

    public void Dispatch(uint surfaceId, Action<PromptSurface> action)
    {
        foreach (var surface in _shown)
        {
            if (surface.SurfaceId == surfaceId)
            {
                action(surface);
                return;
            }
        }
    }

    public void Remove(PromptSurface surface) => _shown.Remove(surface);

    public void Dispose()
    {
        foreach (var surface in _shown.ToArray())
        {
            surface.Dispose();
        }

        _shown.Clear();
    }

    private IOutput? First()
    {
        foreach (var (output, _) in _outputs.Layout.Outputs)
        {
            return output;
        }

        return null;
    }
}
