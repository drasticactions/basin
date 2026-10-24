using Basin.Host;

namespace Basin.Inspector.Ipc;

/// <summary>
/// The overlay and the outputs that the <c>inspector/*</c> methods of <see cref="InspectorIpc"/> act on.
/// </summary>
public sealed class InspectorIpcOptions
{
    /// <summary>
    /// Gets the function that gives the overlay. The methods call it on each request, so it can build the overlay late. A <see langword="null"/> result makes each method answer with an error line.
    /// </summary>
    public required Func<InspectorOverlay?> Overlay { get; init; }

    /// <summary>
    /// Gets the driver whose outputs a request can name.
    /// </summary>
    public required OutputDriver Driver { get; init; }

    /// <summary>
    /// Gets the function that gives the output for a request that names none and an overlay that has no output yet. When it is <see langword="null"/> or gives <see langword="null"/>, the first output is used.
    /// </summary>
    public Func<OutputView?>? DefaultOutput { get; init; }

    /// <summary>
    /// Gets the function that gives the reason why there is no overlay. When it is <see langword="null"/> or gives <see langword="null"/>, the reason is "no ImGui host".
    /// </summary>
    public Func<string?>? Declined { get; init; }
}
