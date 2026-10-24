using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Scene;

/// <summary>
/// Makes the scene-backed capture capabilities as one set and registers them.
/// It registers <see cref="IScreenCapture"/>, <see cref="IDmabufCapture"/> and the appearance, and the stack and exclusion as defaults.
/// </summary>
public sealed class SceneCapturePack : ICapabilityPack
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SceneCapturePack"/> class. It also sets <see cref="Scene.Appearance"/> of the scene.
    /// </summary>
    /// <param name="scene">The scene that the captures draw.</param>
    /// <param name="layout">The layout that places the outputs.</param>
    /// <param name="appearance">The surface appearance to use, or <see langword="null"/> for the default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
    public SceneCapturePack(Scene scene, OutputLayout layout, ISurfaceAppearance? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Index = new ToplevelSceneIndex();
        Stack = new SceneToplevelStack(scene, Index);
        Capture = new SceneScreenCapture(scene, layout) { Index = Index };
        DmabufCapture = new SceneDmabufCapture { Scene = scene, Layout = layout };
        Exclusion = new SceneCaptureExclusion(scene);
        Appearance = appearance ?? new DefaultSurfaceAppearance();
        scene.Appearance = Appearance;
    }

    /// <summary>
    /// Gets the surface appearance that the pack gave to the scene.
    /// </summary>
    public ISurfaceAppearance Appearance { get; }

    /// <summary>
    /// Gets the map from toplevel ids to their scene nodes, shared by the capture and the stack.
    /// </summary>
    public ToplevelSceneIndex Index { get; }

    /// <summary>
    /// Gets the toplevel stack that reads the scene order.
    /// </summary>
    public SceneToplevelStack Stack { get; }

    /// <summary>
    /// Gets the screen capture. The compositor must set its renderer before captures can work.
    /// </summary>
    public SceneScreenCapture Capture { get; }

    /// <summary>
    /// Gets the dmabuf capture. The compositor must track each scene output on it.
    /// </summary>
    public SceneDmabufCapture DmabufCapture { get; }

    /// <summary>
    /// Gets the capture-exclusion answer for synthetic input.
    /// </summary>
    public SceneCaptureExclusion Exclusion { get; }

    /// <summary>
    /// Connects the captures to a toplevel model and fills <see cref="Index"/> as toplevels appear.
    /// </summary>
    /// <param name="toplevels">The toplevel model to watch.</param>
    /// <param name="resolve">A function that finds the scene nodes of the surface of a toplevel, or returns <see langword="null"/>.</param>
    /// <returns>The observer that the model now holds. The caller removes it from the model to stop.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toplevels"/> is <see langword="null"/>.</exception>
    public ToplevelCaptureIndexObserver Attach(
        Capabilities.IToplevelModel toplevels,
        Func<Surface, ToplevelCaptureTrees?> resolve)
    {
        ArgumentNullException.ThrowIfNull(toplevels);
        Capture.Toplevels = toplevels;
        DmabufCapture.Toplevels = toplevels;
        var observer = new ToplevelCaptureIndexObserver(toplevels, Index, Stack, resolve);
        toplevels.AddObserver(observer);
        return observer;
    }

    /// <inheritdoc/>
    public void Register(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services
            .Use<IScreenCapture>(Capture)
            .Use<IDmabufCapture>(DmabufCapture)
            .Use(Appearance)
            .UseDefault<IToplevelStack>(Stack)
            .UseDefault<ICaptureExclusion>(Exclusion);
    }
}
