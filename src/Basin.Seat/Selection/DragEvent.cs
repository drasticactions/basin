using Wayland;

namespace Basin.Seat;

public readonly record struct DragEvent(DataSource? Source, Surface? Origin, Surface? Icon);
