using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Scene;

namespace Basin.Host;

public sealed class ToplevelGeometryReporter : IDisposable
{
    private readonly OutputDriver _driver;
    private readonly Scene.Scene _scene;
    private readonly IToplevelGeometryFeed[] _feeds;
    private readonly List<SurfaceBox> _boxes = [];
    private readonly Action<SceneNode?, Box> _damaged;
    private readonly Action _structureChanged;
    private readonly Action<OutputView> _beforeRepaint;
    private bool _dirty = true;
    private bool _disposed;

    public ToplevelGeometryReporter(OutputDriver driver, Scene.Scene scene, IToplevelModel model)
        : this(driver, scene, model as IToplevelGeometryFeed)
    {
    }

    public ToplevelGeometryReporter(OutputDriver driver, Scene.Scene scene, params IToplevelGeometryFeed?[] feeds)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(feeds);
        _driver = driver;
        _scene = scene;
        _feeds = [.. feeds.OfType<IToplevelGeometryFeed>()];
        _damaged = (_, _) => _dirty = true;
        _structureChanged = () => _dirty = true;
        _beforeRepaint = _ =>
        {
            if (_dirty)
            {
                Report();
            }
        };
        scene.Damaged += _damaged;
        scene.StructureChanged += _structureChanged;
        driver.BeforeRepaint += _beforeRepaint;
    }

    public void Report()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dirty = false;
        _scene.CollectSurfaces(_boxes);
        var boxes = CollectionsMarshal.AsSpan(_boxes);
        foreach (var feed in _feeds)
        {
            feed.ReportFrom(boxes);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scene.Damaged -= _damaged;
        _scene.StructureChanged -= _structureChanged;
        _driver.BeforeRepaint -= _beforeRepaint;
        _boxes.Clear();
    }
}
