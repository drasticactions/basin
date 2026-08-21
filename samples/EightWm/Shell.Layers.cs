using Basin;
using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace EightWm;

internal sealed partial class Shell
{
    private readonly List<(LayerSurface Layer, SceneSurface? Scene, ShellView View)> _layers = [];

    private void AttachLayerShell()
    {
        if (_services.Find<LayerShell>() is not { } shell)
        {
            return;
        }

        shell.NewSurface += OnNewLayerSurface;
    }

    private void OnNewLayerSurface(LayerSurface layer)
    {
        var view = ViewOfOutput(layer.Output) ?? PrimaryView;
        SceneSurface? scene = null;

        layer.InitialCommit += () =>
        {
            var usable = ArrangeLayers(view);
            _ = usable;
        };

        layer.Mapped += () =>
        {
            if (scene is not null)
            {
                return;
            }

            scene = new SceneSurface(TreeFor(view, layer.Layer), layer.Surface);
            for (var i = 0; i < _layers.Count; i++)
            {
                if (ReferenceEquals(_layers[i].Layer, layer))
                {
                    _layers[i] = (layer, scene, view);
                }
            }

            ArrangeLayers(view);
            Relayout(view);
            Console.WriteLine($"LAYER + {layer.Namespace} {layer.Layer}");
        };

        layer.Unmapped += () =>
        {
            if (scene is { IsDestroyed: false } live)
            {
                live.Destroy();
            }

            scene = null;
            Forget(layer);
            ArrangeLayers(view);
            Relayout(view);
            Console.WriteLine($"LAYER - {layer.Namespace}");
        };

        layer.Committed += () => ArrangeLayers(view);
        _layers.Add((layer, null, view));
    }

    private void Forget(LayerSurface layer)
    {
        for (var i = _layers.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_layers[i].Layer, layer))
            {
                _layers.RemoveAt(i);
            }
        }
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

    private readonly List<(LayerSurface Layer, SceneSurface? Scene)> _arrangeScratch = [];

    private Box ArrangeLayers(ShellView view)
    {
        _arrangeScratch.Clear();
        foreach (var (layer, scene, owner) in _layers)
        {
            if (ReferenceEquals(owner, view))
            {
                _arrangeScratch.Add((layer, scene));
            }
        }

        var box = view.Box;
        if (_arrangeScratch.Count == 0)
        {
            view.UsableArea = new Box(0, 0, box.Width, box.Height);
            return view.UsableArea;
        }

        var usable = LayerArrangement.Arrange(box, _arrangeScratch);
        view.UsableArea = usable;
        return usable;
    }

    internal void RearrangeLayers()
    {
        foreach (var view in Views)
        {
            ArrangeLayers(view);
        }
    }
}
