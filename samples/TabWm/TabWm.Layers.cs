using Basin;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;

namespace TabWm;

internal sealed partial class TabWm
{
    private readonly Dictionary<IOutput, Box> _usable = [];
    private LayerShellSceneDriver _layerDriver = null!;
    private SessionLockSceneDriver _lockDriver = null!;
    private LayerSurface? _layerKeyboard;

    private void CreateLayerDrivers()
    {
        _layerDriver = new LayerShellSceneDriver(_services, _layout, layer => LayerTree(layer.Layer));
        _lockDriver = new SessionLockSceneDriver(_services, _layers.Lock, _layout, OnLockChanged);
    }

    private void WireLayerShell()
    {
        _layerDriver.Accept = _ => _outputs.Views.Count > 0;
        _layerDriver.DefaultOutput = _ => _outputs.Views.Count > 0 ? _outputs.Views[0].Global : null;
        _layerDriver.TrackPopups(Shell);
        _layerDriver.SceneCreated += OnLayerMapped;
        _layerDriver.PopupSceneCreated += (_, _, _) => RefreshSurfaceLuts();
        _layerDriver.Removed += OnLayerRemoved;
        _layerDriver.UsableAreaChanged += (output, usable) =>
        {
            var box = _layout.BoxOf(output);
            _usable[output] = usable.Translated(box.X, box.Y);
            Arrange();
        };
        _layerDriver.Arranged += _outputs.ScheduleAll;
    }

    private SceneTree LayerTree(LayerKind kind) => kind switch
    {
        LayerKind.Background => _layers.LayerBackground,
        LayerKind.Bottom => _layers.LayerBottom,
        LayerKind.Top => _layers.LayerTop,
        _ => _layers.LayerOverlay,
    };

    private void OnLayerMapped(LayerSurface layer, SceneSurface scene)
    {
        RefreshSurfaceLuts();
        BasinReport.Line($"LAYER + {layer.Namespace} {layer.Layer}");
        if (layer.KeyboardInteractivity == ZwlrLayerSurfaceV1.KeyboardInteractivity.None || _layers.IsLocked)
        {
            return;
        }

        _layerKeyboard = layer;
        KeyboardTarget = null;
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
        if (_layers.IsLocked || KeyboardTarget is not null)
        {
            return;
        }

        Seat.Keyboard.NotifyEnter(_focused is { Mapped: true } focused ? focused.Surface : null);
    }

    private void OnLockChanged(bool locked)
    {
        _layers.SetLocked(locked);
        BasinReport.Line($"LOCK {(locked ? "locked" : "unlocked")}");
        if (!locked)
        {
            RestoreFocus();
        }

        _outputs.ScheduleAll();
    }
}
