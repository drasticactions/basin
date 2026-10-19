namespace Basin.Capabilities;

public readonly record struct RecordFrameStats(
    int Index,
    Box At,
    double Luma,
    double Peak,
    int Delta,
    IReadOnlyList<Box> Planes,
    bool Unmapped);
