namespace Basin.Scene3D;

/// <summary>
/// The kind of space that a <see cref="WorldRoom"/> is. The library treats both kinds the same.
/// </summary>
public enum WorldRoomKind
{
    /// <summary>
    /// A room.
    /// </summary>
    Room,
    /// <summary>
    /// A corridor that joins rooms.
    /// </summary>
    Corridor,
}
