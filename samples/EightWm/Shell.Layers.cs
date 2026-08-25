using Basin;
using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.Xdg;

using Basin.Diagnostics;

namespace EightWm;

internal sealed partial class Shell
{
    private LayerShellSceneDriver? _layerDriver;
    private ShellView? _layerRelayout;

    private void AttachLayerShell()
    {
        if (_services.Find<LayerShell>() is not { } shell)
        {
            return;
        }

        _layerDriver = new LayerShellSceneDriver(
            shell, _layout, layer => TreeFor(ViewOfOutput(layer.Output) ?? PrimaryView, layer.Layer));
        _layerDriver.TrackPopups(Shells);
        _layerDriver.SceneCreated += (layer, _) =>
        {
            var view = ViewOfOutput(layer.Output) ?? PrimaryView;
            Relayout(view);
            BasinReport.Line($"LAYER + {layer.Namespace} {layer.Layer}");
        };
        _layerDriver.Removed += layer =>
        {
            _layerRelayout = ViewOfOutput(layer.Output) ?? PrimaryView;
            BasinReport.Line($"LAYER - {layer.Namespace}");
        };
        _layerDriver.Arranged += () =>
        {
            if (_layerRelayout is { } view)
            {
                _layerRelayout = null;
                Relayout(view);
            }
        };
        _layerDriver.UsableAreaChanged += (output, usable) =>
        {
            foreach (var view in Views)
            {
                if (ReferenceEquals(view.Driver.Output, output))
                {
                    view.UsableArea = usable;
                }
            }
        };
    }

    private static SceneTree TreeFor(ShellView view, LayerKind kind) => kind switch
    {
        LayerKind.Background => view.Background,
        LayerKind.Bottom => view.Apps,
        LayerKind.Top => view.Chrome,
        _ => view.Overlay,
    };

    private ShellView? ViewOfOutput(OutputGlobal? global)
    {
        if (global is null)
        {
            return null;
        }

        foreach (var view in Views)
        {
            if (ReferenceEquals(view.Global, global))
            {
                return view;
            }
        }

        return null;
    }

    internal void RearrangeLayers() => _layerDriver?.Rearrange();
}
