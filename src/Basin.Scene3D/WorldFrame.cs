namespace Basin.Scene3D;

/// <summary>
/// The input to one <see cref="IWorldRenderer.Render"/> call: the world, the view of one output, the frame time, the visible set and the settings.
/// </summary>
public readonly struct WorldFrame
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorldFrame"/> struct.
    /// </summary>
    /// <param name="world">The world to draw.</param>
    /// <param name="view">The camera view of the output's slice.</param>
    /// <param name="tick">The frame time. Light flicker reads its target present time.</param>
    /// <param name="visible">The set that <see cref="WorldVisibility.Collect"/> filled for <paramref name="view"/>.</param>
    /// <param name="settings">The portal depth, room depth, light count and sampling limits.</param>
    /// <exception cref="ArgumentNullException"><paramref name="world"/>, <paramref name="visible"/> or <paramref name="settings"/> is <see langword="null"/>.</exception>
    public WorldFrame(World world, in WorldView view, FrameTick tick, WorldVisibleSet visible, WorldRenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(settings);
        World = world;
        View = view;
        Tick = tick;
        Visible = visible;
        Settings = settings;
    }

    /// <summary>
    /// Gets the world to draw.
    /// </summary>
    public World World { get; }

    /// <summary>
    /// Gets the camera view of the output's slice of the layout.
    /// </summary>
    public WorldView View { get; }

    /// <summary>
    /// Gets the frame time. The light intensity comes from it, so a fixed tick gives a stable image.
    /// </summary>
    public FrameTick Tick { get; }

    /// <summary>
    /// Gets the rooms, windows, props and portals that the main view can see.
    /// </summary>
    public WorldVisibleSet Visible { get; }

    /// <summary>
    /// Gets the limits that the pass applies to this frame.
    /// </summary>
    public WorldRenderSettings Settings { get; }
}
