namespace Basin.Scene3D;

/// <summary>
/// A room or corridor: a baked structure mesh with its lights, doorways, portals and pick blockers. A pass draws a room in one draw.
/// </summary>
public sealed class WorldRoom
{
    private WorldMesh? _structure;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldRoom"/> class.
    /// </summary>
    /// <param name="id">The consumer's identifier for the room.</param>
    /// <param name="kind">Whether the room is a room or a corridor.</param>
    /// <param name="bounds">The world-space box that the room fills.</param>
    public WorldRoom(int id, WorldRoomKind kind, WorldBounds bounds)
    {
        Id = id;
        Kind = kind;
        Bounds = bounds;
    }

    /// <summary>
    /// Gets the consumer's identifier for the room.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Gets whether the room is a room or a corridor.
    /// </summary>
    public WorldRoomKind Kind { get; }

    /// <summary>
    /// Gets or sets the world-space box that the room fills. The walk culls the room by it and finds the eye's room by it.
    /// </summary>
    public WorldBounds Bounds { get; set; }

    /// <summary>
    /// Gets a counter that each set of <see cref="Structure"/> increments.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Gets or sets the baked mesh of the walls, floor and roof, in world space. The consumer bakes it when the room changes, never on each frame.
    /// </summary>
    public WorldMesh? Structure
    {
        get => _structure;
        set
        {
            _structure = value;
            Version++;
        }
    }

    /// <summary>
    /// Gets the lights of the room. A pass lights a level with the nearest lights of its visible rooms.
    /// </summary>
    public List<WorldLight> Lights { get; } = [];

    /// <summary>
    /// Gets the doorways that join this room to others. The visibility walk follows them.
    /// </summary>
    public List<WorldDoorway> Doorways { get; } = [];

    /// <summary>
    /// Gets the world-space boxes that stop a pick ray, for example walls and pillars.
    /// </summary>
    public List<WorldBounds> Blockers { get; } = [];

    /// <summary>
    /// Gets the windows that the consumer places in this room. The library does not read this list. It reads <see cref="WorldWindow.Room"/>.
    /// </summary>
    public List<WorldWindow> Windows { get; } = [];

    /// <summary>
    /// Gets the portals on the walls of this room. The visibility walk collects them from here.
    /// </summary>
    public List<WorldPortal> Portals { get; } = [];

    /// <summary>
    /// Gets or sets a value that the consumer owns. The library does not read it.
    /// </summary>
    public object? Tag { get; set; }

    /// <summary>
    /// Tests a point against <see cref="Bounds"/>. The faces are inside.
    /// </summary>
    /// <param name="point">The point in world space.</param>
    /// <returns><see langword="true"/> when the room holds the point.</returns>
    public bool Contains(System.Numerics.Vector3 point) => Bounds.Contains(point);
}
