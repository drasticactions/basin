using System.Diagnostics.CodeAnalysis;

namespace Basin.Capabilities;

/// <summary>
/// Creates outputs that no physical display backs, for example for screen sharing to a second virtual monitor.
/// </summary>
public interface IVirtualOutputFactory
{
    /// <summary>
    /// Creates a virtual output.
    /// </summary>
    /// <param name="name">The output name.</param>
    /// <param name="description">A readable description.</param>
    /// <param name="width">The mode width in pixels.</param>
    /// <param name="height">The mode height in pixels.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="output">The new output on success, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when a size or the scale is not positive, or the backend cannot create outputs.</returns>
    bool TryCreate(string name, string description, int width, int height,
                   double scale, [NotNullWhen(true)] out IOutput? output);

    /// <summary>
    /// Destroys an output that <see cref="TryCreate"/> created. Another output is ignored.
    /// </summary>
    /// <param name="output">The output to destroy.</param>
    void Destroy(IOutput output);
}
