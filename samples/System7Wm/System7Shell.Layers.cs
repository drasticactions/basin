using Basin;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;

namespace System7Wm;

internal sealed partial class System7Shell
{
    private readonly Dictionary<IOutput, Box> _usable = [];
    private LayerShellSceneDriver _layerDriver = null!;
    private SessionLockSceneDriver _lockDriver = null!;
    private LayerSurface? _layerKeyboard;
    private bool _locked;

    private void CreateLayerDrivers()
    {
        _layerDriver = new LayerShellSceneDriver(_services, _layout, layer => LayerTree(layer.Layer));
        _lockDriver = new SessionLockSceneDriver(_services, _layers.Lock, _layout, SetLocked);
    }

    private void AttachLayers()
    {
        _layerDriver.Accept = _ => _outputs.Views.Count > 0;
        _layerDriver.DefaultOutput = _ => MainView?.Global;
        _layerDriver.TrackPopups(Shells);
        _layerDriver.SceneCreated += OnLayerMapped;
        _layerDriver.PopupSceneCreated += (_, _, _) => RefreshSurfaceLuts();
        _layerDriver.Removed += OnLayerRemoved;
        _layerDriver.UsableAreaChanged += (output, usable) =>
        {
            var box = _layout.BoxOf(output);
            _usable[output] = usable.Translated(box.X, box.Y);
        };
        _layerDriver.Arranged += _outputs.ScheduleAll;

        _lockDriver.Locked += () => BasinReport.Line($"LOCKED");
        _lockDriver.Unlocked += () =>
        {
            RestoreKeyboard();
            BasinReport.Line($"UNLOCKED");
        };
        _lockDriver.Abandoned += () => BasinReport.Line($"LOCK ABANDONED (staying blanked)");
    }

    private SceneTree LayerTree(LayerKind kind) => kind switch
    {
        LayerKind.Background => _layers.LayerBackground,
        LayerKind.Bottom => _layers.LayerBottom,
        LayerKind.Top => _layers.LayerTop,
        _ => _layers.LayerOverlay,
    };

    private void RelayoutLayers()
    {
        _layerDriver.Rearrange();
        _lockDriver.Reconfigure();
    }

    private Box UsableOf(IOutput output, Box box) =>
        _usable.TryGetValue(output, out var usable) && !usable.IsEmpty ? usable : box;

    private void OnLayerMapped(LayerSurface layer, SceneSurface scene)
    {
        RefreshSurfaceLuts();
        BasinReport.Line($"LAYER + {layer.Namespace} {layer.Layer}");
        if (layer.KeyboardInteractivity == ZwlrLayerSurfaceV1.KeyboardInteractivity.None || _locked)
        {
            return;
        }

        _layerKeyboard = layer;
        _seat.Router.SetKeyboardFocus(null);
        Seat.Keyboard.NotifyEnter(layer.Surface);
    }

    private void OnLayerRemoved(LayerSurface layer)
    {
        BasinReport.Line($"LAYER - {layer.Namespace}");
        if (!ReferenceEquals(_layerKeyboard, layer))
        {
            return;
        }

        _layerKeyboard = null;
        if (!_locked)
        {
            RestoreKeyboard();
        }
    }

    private void SetLocked(bool locked)
    {
        _locked = locked;
        _layers.SetLocked(locked);
        if (locked)
        {
            CancelDrag();
            _inspector?.Close();
            _seat.Router.SetKeyboardFocus(null);
            _seat.Router.PointerLeave();
        }

        _seat.Refresh();
        _outputs.ScheduleAll();
    }

    private void RestoreKeyboard()
    {
        if (_layerKeyboard is { IsMapped: true } layer)
        {
            Seat.Keyboard.NotifyEnter(layer.Surface);
            return;
        }

        if (_active is { Mapped: true } active)
        {
            FocusKeyboard(active);
            return;
        }

        Seat.Keyboard.NotifyClearFocus();
    }
}
