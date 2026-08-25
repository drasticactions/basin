using Basin;
using Basin.Scene;
using Basin.XWayland;

using Basin.Diagnostics;

namespace Westonia;

internal sealed class ShellXWayland : IDisposable
{
    private readonly WestonShell _shell;
    private readonly ShellLayers _layers;
    private readonly BasinLogger _log;
    private readonly XWaylandSceneDriver _driver = new();
    private readonly Func<SceneTree> _workspace;

    public ShellXWayland(
        WestonShell shell,
        ShellLayers layers,
        Func<SceneTree> workspace,
        Func<Box> area,
        BasinLogger log)
    {
        _shell = shell;
        _layers = layers;
        _workspace = workspace;
        _log = log;
        _driver.CenterArea = area;
        _driver.ManagedParent = _ => _workspace();
        _driver.OverrideRedirectParent = _ => _layers.InputPanel;
        _driver.ActivationRequested += Activate;
        _driver.Adopted += OnAdopted;
        _driver.Removed += (_, _) => Changed?.Invoke();
    }

    public int Count => _driver.Count;

    public SceneNode? TreeOf(Surface surface) => _driver.SceneOf(surface)?.Tree;

    public Action? Changed { get; set; }

    public void Attach(XWaylandWm wm) => _driver.Attach(wm);

    public void Dispose() => _driver.Dispose();

    private void OnAdopted(XWaylandWindow window, SceneSurface scene, bool managed)
    {
        Changed?.Invoke();
        _log.Info($"mapped X window {window.WindowId} \"{window.Title}\"");
        if (managed)
        {
            Activate(window);
        }
    }

    private void Activate(XWaylandWindow window)
    {
        if (window.Surface is { } surface && window.WantsFocus)
        {
            _shell.KeyboardTarget = null;
            _shell.Seat?.Keyboard.NotifyEnter(surface);
        }
    }
}
