namespace Basin.Seat;

public readonly record struct TouchHit(Surface? Surface, double LocalX, double LocalY, object? Token);
