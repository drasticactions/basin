using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Scene;

namespace Basin.Host;

/// <summary>
/// Sends the scene boxes of the surfaces to toplevel geometry feeds before a repaint, when the scene changed since the last report.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ToplevelGeometryReporter"/> class for one toplevel model. A model that is not an <see cref="IToplevelGeometryFeed"/> gets no reports.
    /// </summary>
    /// <param name="driver">The driver whose repaints trigger the reports.</param>
    /// <param name="scene">The scene to collect surface boxes from.</param>
    /// <param name="model">The toplevel model to report to.</param>
    public ToplevelGeometryReporter(OutputDriver driver, Scene.Scene scene, IToplevelModel model)
        : this(driver, scene, model as IToplevelGeometryFeed)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToplevelGeometryReporter"/> class for a set of feeds.
    /// </summary>
    /// <param name="driver">The driver whose repaints trigger the reports.</param>
    /// <param name="scene">The scene to collect surface boxes from.</param>
    /// <param name="feeds">The feeds to report to. Null entries are ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="driver"/>, <paramref name="scene"/> or <paramref name="feeds"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Collects the surface boxes from the scene now and sends them to every feed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The reporter is disposed.</exception>
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

    /// <summary>
    /// Unsubscribes from the scene and the driver. No reports follow.
    /// </summary>
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
