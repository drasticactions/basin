using Basin;
using Basin.Capabilities;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private readonly List<Window> _reframedWindows = [];
    private readonly List<XWindow> _reframedXWindows = [];

    private void WireRendererRecovery()
    {
        _driver.RendererFactory = () => Basin.Renderers.RendererCatalog.Create(_rendererName, _renderNode ?? string.Empty);
        _host.RendererReplaced += OnRendererReplaced;
        _host.RendererRebuilt += OnRendererRebuilt;
    }

    private void OnRendererReplaced(RendererReplacement replacement)
    {
        _report.Line($"RENDERER lost {replacement.Old.Renderer.GetType().Name}");
        _renderer = replacement.New.Renderer;
        _allocator = replacement.New.DeviceAllocator;

        _reframedWindows.Clear();
        foreach (var window in _windows)
        {
            if (window.ReleaseFrame())
            {
                _reframedWindows.Add(window);
            }
        }

        _reframedXWindows.Clear();
        foreach (var xwindow in _xwindows)
        {
            if (xwindow.Frame is not null)
            {
                xwindow.DisposeFrame();
                _reframedXWindows.Add(xwindow);
            }
        }

        _quillDemo?.Dispose();
        _quillDemo = null;
        DisposeImGui();
        _imguiDeclined = null;
        DisposeSettings();
        if (_quillHost is { } quill)
        {
            _frameHosts?.Remove(quill);
            quill.Dispose();
            _quillHost = null;
        }

        _quillDeclined = null;
        (_uiHost as IRendererBound)?.OnRendererReplaced(replacement);
        (_glassEffect as IRendererBound)?.OnRendererReplaced(replacement);
        (_blurEffect as IRendererBound)?.OnRendererReplaced(replacement);
        _xSceneDriver.OnRendererReplaced(replacement);
        _hyprCtm?.ReleaseShader();

        foreach (var view in Views)
        {
            if (view.Scene is { } output)
            {
                _post.Remove(output);
                _shader.Remove(output);
            }

            if (view.Tag is OutputPolicy)
            {
                view.Canvas.Glow.Dispose(_glowShaders);
                OverviewOf(view).StepGlow.Dispose(_glowShaders);
            }
        }

        foreach (var shader in _glowShaders)
        {
            shader.Dispose();
        }

        _glowShaders.Clear();
        _post.Dispose();
        _shader.Dispose();
        _effects.FireShaderHandle = null;
        _fireShader?.Dispose();
        _fireShader = null;
        _fireShaderTried = false;
    }

    private void OnRendererRebuilt(RendererReplacement replacement)
    {
        (_uiHost as IRendererBound)?.OnRendererRebuilt(replacement);
        (_glassEffect as IRendererBound)?.OnRendererRebuilt(replacement);
        (_blurEffect as IRendererBound)?.OnRendererRebuilt(replacement);
        _xSceneDriver.OnRendererRebuilt(replacement);
        if (_backend is { } nested && _host.Parent is not null)
        {
            nested.RenderDevice = _renderer.Device;
        }

        _post = new PostEffects(_renderer, _rendererName, _log);
        _post.Configure(_config);
        _shader = new ScreenShader(_renderer, _rendererName, _allocator, _log);
        _shader.Configure(_config);
        foreach (var view in Views)
        {
            AddPostStage(view);
            LayoutCanvas(view);
        }

        ApplyEffectShaders();
        _hyprCtm?.RestoreShader();
        foreach (var window in _reframedWindows)
        {
            window.SetDecorated(true);
        }

        foreach (var xwindow in _reframedXWindows)
        {
            xwindow.UpdateDecorations();
        }

        _reframedWindows.Clear();
        _reframedXWindows.Clear();
        _report.Line($"RENDERER rebuilt {_renderer.GetType().Name} rebuilds={_driver.RendererRebuilds}");
    }
}
