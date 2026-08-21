using Basin;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Scene;
using Basin.UI.Avalonia;
using Microsoft.Extensions.Logging;
using Westonia.Shell;

namespace Westonia;

internal sealed class ShellLock : IDisposable
{
    private readonly AvaloniaUIHost _host;
    private readonly ShellLayers _layers;
    private readonly WestonShell _shell;
    private readonly WestonIni _ini;
    private readonly ILogger _log;
    private readonly Func<Box> _area;
    private readonly UISurfaceIndex _index;
    private AvaloniaUISurface? _dialog;
    private UISurfaceNode? _dialogNode;
    private bool _disposed;

    public ShellLock(
        AvaloniaUIHost host,
        ShellLayers layers,
        WestonShell shell,
        WestonIni ini,
        ILogger log,
        Func<Box> area,
        UISurfaceIndex index)
    {
        _index = index;
        _host = host;
        _layers = layers;
        _shell = shell;
        _ini = ini;
        _log = log;
        _area = area;
    }

    public bool IsLocked => _layers.IsLocked;

    public bool ClientLocked { get; private set; }

    public Action? Changed { get; set; }

    public Func<IOutput, Box> LayoutOf { get; set; } = _ => new Box(0, 0, 1280, 720);

    public Func<double> Scale { get; set; } = () => 1.0;

    public void AttachSessionLock(SessionLockManager manager)
    {
        manager.Locked += () =>
        {
            if (IsLocked && !ClientLocked)
            {
                _log.LogWarning("a session lock client arrived while the shell already holds the lock");
            }

            ClientLocked = true;
            CloseDialog();
            _layers.SetLocked(true);
            Changed?.Invoke();
        };

        manager.Unlocked += () =>
        {
            ClientLocked = false;
            _layers.SetLocked(false);
            Changed?.Invoke();
        };

        manager.Abandoned += () =>
        {
            _log.LogWarning("the session lock client died; the screen stays locked");
            _layers.SetLocked(true);
            Changed?.Invoke();
        };

        manager.NewLockSurface += surface =>
        {
            var scene = new SceneSurface(_layers.Lock, surface.Surface);
            var box = LayoutOf(surface.Output.Output);
            scene.Tree.SetPosition(box.X, box.Y);
            surface.Mapped += () => _shell.Seat?.Keyboard.NotifyEnter(surface.Surface);
        };
    }

    public bool CanLock => _ini.Shell.Locking && !ClientLocked;

    public void Lock()
    {
        if (_disposed || IsLocked || !CanLock)
        {
            return;
        }

        _layers.SetLocked(true);
        _shell.Seat?.Keyboard.NotifyClearFocus();

        if (_shell.Client is { } client)
        {
            client.PrepareLockSurface();
            _log.LogInformation("asked the shell client for a lock surface");
            Changed?.Invoke();
            return;
        }

        ShowDialog();
        Changed?.Invoke();
    }

    public void Unlock()
    {
        if (!IsLocked || ClientLocked)
        {
            return;
        }

        CloseDialog();
        _layers.SetLocked(false);
        _shell.KeyboardTarget = null;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        CloseDialog();
    }

    private void ShowDialog()
    {
        var box = _area();
        _dialog = _host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = box.Width,
            Height = box.Height,
            Scale = Scale(),
        }) as AvaloniaUISurface;
        if (_dialog is null)
        {
            return;
        }

        _dialog.Content = new UnlockView { DataContext = new UnlockModel(Unlock) };
        _dialog.SetPosition(box.X, box.Y);
        _dialogNode = new UISurfaceNode(_layers.Lock, _dialog, _index) { PreciseDamage = true };
        _dialogNode.SetPosition(box.X, box.Y);
        _shell.KeyboardTarget = _dialog;
    }

    private void CloseDialog()
    {
        if (ReferenceEquals(_shell.KeyboardTarget, _dialog))
        {
            _shell.KeyboardTarget = null;
        }

        _dialogNode?.Dispose();
        _dialogNode = null;
        _dialog?.Dispose();
        _dialog = null;
    }

    public AvaloniaUISurface? Dialog => _dialog;
}
