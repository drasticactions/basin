namespace Basin;

/// <summary>
/// A CPU view of a buffer's pixels. It is valid only between <see cref="IBuffer.BeginDataAccess"/> and <see cref="IBuffer.EndDataAccess"/>.
/// </summary>
/// <param name="Data">The address of the first row of the first plane.</param>
/// <param name="Stride">The distance in bytes between the starts of two rows.</param>
/// <param name="Format">The pixel layout at <paramref name="Data"/>.</param>
public readonly record struct BufferDataView(nint Data, int Stride, DrmFormat Format);
