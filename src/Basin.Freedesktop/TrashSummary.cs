namespace Basin.Freedesktop;

/// <summary>
/// The totals that <see cref="FreedesktopTrash.Summary"/> gives for the home trash.
/// </summary>
/// <param name="Items">The number of items in the trash.</param>
/// <param name="Bytes">The total size of all items, in bytes.</param>
public readonly record struct TrashSummary(int Items, long Bytes);
