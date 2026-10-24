namespace Basin.Diagnostics;

/// <summary>
/// The result of <see cref="ImageCompare.Compare"/>.
/// </summary>
/// <param name="SameSize">A value that is false when the two images have different dimensions. The other fields then count every pixel as changed.</param>
/// <param name="Changed">The number of pixels whose difference is more than the tolerance.</param>
/// <param name="MaxDelta">The largest difference of one channel at one pixel, from 0 to 255.</param>
/// <param name="ChangedBox">The smallest box that contains every changed pixel. It is empty when no pixel changed.</param>
public readonly record struct ImageComparison(bool SameSize, int Changed, int MaxDelta, Box ChangedBox);
