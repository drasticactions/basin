namespace Basin.Diagnostics;

public readonly record struct ImageComparison(bool SameSize, int Changed, int MaxDelta, Box ChangedBox);
