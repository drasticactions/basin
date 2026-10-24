namespace Basin.Capabilities;

/// <summary>
/// The statistics of one recorded frame.
/// </summary>
/// <param name="Index">The zero-based index of the frame.</param>
/// <param name="At">The recorded box in output physical pixels.</param>
/// <param name="Luma">The mean luma of the frame, from 0 to 255.</param>
/// <param name="Peak">The mean luma of the brightest 2 percent of pixels, from 0 to 255.</param>
/// <param name="Delta">The number of pixels with a color channel that changed by more than 8 since the previous frame. It is 0 for the first frame.</param>
/// <param name="Planes">The boxes of the planes that the output presented for the frame.</param>
/// <param name="Unmapped">Whether a post stage did not map the followed box for the frame.</param>
public readonly record struct RecordFrameStats(
    int Index,
    Box At,
    double Luma,
    double Peak,
    int Delta,
    IReadOnlyList<Box> Planes,
    bool Unmapped);
