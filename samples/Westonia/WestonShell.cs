using Basin;
using Basin.Scene;
using Basin.Shell.Weston;
using Microsoft.Extensions.Logging;

namespace Westonia;

internal sealed partial class WestonShell : IDisposable
{
    private readonly ShellLayers _layers;
    private readonly WestonIni _ini;
    private readonly ILogger _log;
    private readonly Dictionary<IOutput, SceneSurface> _clientBackgrounds = [];
    private readonly Dictionary<IOutput, SceneSurface> _clientPanels = [];
    private readonly Dictionary<IOutput, SceneSurface> _screensavers = [];
    private SceneSurface? _lockSurface;
    private Surface? _grabSurface;
    private bool _desktopReady;
    private bool _disposed;

    public WestonShell(ShellLayers layers, WestonIni ini, ILogger log)
    {
        _layers = layers;
        _ini = ini;
        _log = log;
    }

    public AvaloniaShell? Avalonia { get; set; }

    public UISurfaceRouter? Input { get; set; }

    public Basin.Capabilities.IUISurface? KeyboardTarget
    {
        get => Input?.KeyboardFocus;
        set => Input?.SetKeyboardFocus(value);
    }

    public IShellClient? Client { get; set; }

    public bool DesktopIsReady => _desktopReady;

    public int ClientBackgrounds => _clientBackgrounds.Count;

    public int ClientPanels => _clientPanels.Count;

    public Surface? GrabSurface => _grabSurface;

    public Func<IOutput, Box>? OutputPlacement { get; set; }

    public void AdoptBackground(IOutput output, Surface surface)
    {
        Avalonia?.Remove(output);
        Replace(_clientBackgrounds, output, surface, _layers.Background, OutputPlacement?.Invoke(output));
        SizeToOutput(output, surface);
    }

    public void AdoptPanel(IOutput output, Surface surface)
    {
        Replace(_clientPanels, output, surface, _layers.Panel, OutputPlacement?.Invoke(output));
        SizeToOutput(output, surface);
    }

    private void SizeToOutput(IOutput output, Surface surface)
    {
        var box = OutputPlacement?.Invoke(output) ?? new Box(0, 0, 1280, 720);
        Client?.Configure(surface, 0, box.Width, box.Height);
    }

    public void SetPanelPosition(ShellPanelPosition position)
    {
        if (Avalonia is null)
        {
            return;
        }

        Avalonia.PanelPosition = position switch
        {
            ShellPanelPosition.Bottom => PanelPosition.Bottom,
            ShellPanelPosition.Left => PanelPosition.Left,
            ShellPanelPosition.Right => PanelPosition.Right,
            _ => PanelPosition.Top,
        };
    }

    public void AdoptLockSurface(Surface surface)
    {
        _lockSurface?.Tree.Destroy();
        _lockSurface = new SceneSurface(_layers.Lock, surface);
        _layers.SetLocked(true);
        if (Seat is { } seat)
        {
            seat.Keyboard.NotifyEnter(surface);
        }

        if (OutputPlacement is { } lookup && LockOutput is { } output)
        {
            var box = lookup(output);
            _lockSurface.Tree.SetPosition(box.X, box.Y);
            Client?.Configure(surface, 0, box.Width, box.Height);
        }
    }

    public IOutput? LockOutput { get; set; }

    public void Unlock()
    {
        _lockSurface?.Tree.Destroy();
        _lockSurface = null;
        _layers.SetLocked(false);
    }

    public void AdoptGrabSurface(Surface surface) => _grabSurface = surface;

    public void DesktopReady()
    {
        _desktopReady = true;
        _log.LogInformation("the shell reports the desktop is ready");
    }

    public void AdoptScreensaverSurface(IOutput output, Surface surface)
    {
        Replace(_screensavers, output, surface, _layers.Background, OutputPlacement?.Invoke(output));
        SizeToOutput(output, surface);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lockSurface?.Tree.Destroy();
        _lockSurface = null;
        foreach (var map in new[] { _clientBackgrounds, _clientPanels, _screensavers })
        {
            foreach (var entry in map.Values)
            {
                entry.Tree.Destroy();
            }

            map.Clear();
        }
    }

    private static void Replace(
        Dictionary<IOutput, SceneSurface> map,
        IOutput output,
        Surface surface,
        SceneTree layer,
        Box? box)
    {
        if (map.Remove(output, out var previous))
        {
            previous.Tree.Destroy();
        }

        var scene = new SceneSurface(layer, surface);
        if (box is { } placement)
        {
            scene.Tree.SetPosition(placement.X, placement.Y);
        }

        map[output] = scene;
    }
}
