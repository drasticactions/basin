namespace Basin;

/// <summary>
/// Represents a copy of a buffer from another GPU into a buffer that the renderer can import.
/// </summary>
public interface ICrossDeviceConversion : IDisposable
{
    /// <summary>
    /// Gets the converted buffer that the renderer imports. The conversion owns it.
    /// </summary>
    IBuffer Buffer { get; }

    /// <summary>
    /// Copies the source buffer again after its content changed.
    /// </summary>
    void Refresh();
}
