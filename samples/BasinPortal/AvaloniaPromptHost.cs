using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Portal.Client;
using Basin.WindowManager.Skia.Protocol;
using Basin.Portal;

namespace BasinPortal;

internal sealed class AvaloniaPromptHost : IPortalPromptHost, IDisposable
{
    private readonly ClientGlobals _globals;
    private readonly ZwlrLayerShellV1? _layerShell;
    private readonly ClientOutputs _outputs;
    private readonly ClientLoop _loop;
    private readonly BasinLogger _log;
    private readonly List<AvaloniaPromptSurface> _shown = [];

    public AvaloniaPromptHost(ClientGlobals globals, ZwlrLayerShellV1? layerShell, ClientOutputs outputs, ClientLoop loop, BasinLogger log)
    {
        _globals = globals;
        _layerShell = layerShell;
        _outputs = outputs;
        _loop = loop;
        _log = log;
    }

    public int Open => _shown.Count;

    public IOutput? OutputFor(string parentWindow) => First();

    public void Show(IUISurface surface, IOutput output)
    {
        if (_layerShell is not { } layerShell || _globals.Compositor is not { } compositor ||
            _globals.Shm is not { } shm || output is not ClientOutput portal)
        {
            _log.Warn($"no layer shell to show a portal prompt");
            throw new InvalidOperationException("the compositor serves no layer shell to show a portal prompt on");
        }

        var box = _outputs.Layout.BoxOf(output);
        var size = surface.Size;
        var cover = size.Width >= box.Width && size.Height >= box.Height;
        var shown = new AvaloniaPromptSurface(this, compositor, shm, layerShell, portal, surface, cover, _loop);
        _shown.Add(shown);
        _loop.RequestFlush();
    }

    public void Hide(IUISurface surface)
    {
        foreach (var shown in _shown.ToArray())
        {
            if (ReferenceEquals(shown.Surface, surface))
            {
                shown.Dispose();
            }
        }
    }

    public void Dispatch(uint surfaceId, Action<AvaloniaPromptSurface> action)
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

    public void Remove(AvaloniaPromptSurface surface) => _shown.Remove(surface);

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
