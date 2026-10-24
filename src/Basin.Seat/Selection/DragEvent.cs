using Wayland;

namespace Basin.Seat;

/// <summary>
/// Describes a drag when it starts.
/// </summary>
/// <param name="Source">The dragged data source, or <see langword="null"/> when a client drags with no source.</param>
/// <param name="Origin">The surface where a client started the drag, or <see langword="null"/> when the compositor started it through <see cref="SeatDataDevice.StartDrag"/>.</param>
/// <param name="Icon">The surface that follows the pointer, or <see langword="null"/> for no icon.</param>
public readonly record struct DragEvent(DataSource? Source, Surface? Origin, Surface? Icon);
