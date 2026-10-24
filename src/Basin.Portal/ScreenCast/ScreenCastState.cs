using Basin.Capabilities;

namespace Basin.Portal;

/// <summary>
/// The screen cast part of a session: the choices from <c>SelectSources</c> and the published streams.
/// </summary>
public sealed class ScreenCastState
{
    private readonly List<ScreenCastStream> _streams = [];

    /// <summary>
    /// Gets the source type bits that the application asked for. The default is <see cref="ScreenCastSourceKind.Monitor"/>.
    /// </summary>
    public uint Types { get; internal set; } = (uint)ScreenCastSourceKind.Monitor;

    /// <summary>
    /// Gets a value indicating whether the application accepts more than one source.
    /// </summary>
    public bool Multiple { get; internal set; }

    /// <summary>
    /// Gets the cursor mode that the application chose. The default is <see cref="ScreencastCursorMode.Hidden"/>.
    /// </summary>
    public ScreencastCursorMode CursorMode { get; internal set; } = ScreencastCursorMode.Hidden;

    /// <summary>
    /// Gets the persist mode that the application asked for: 0 for none, 1 for this run, 2 until revoked.
    /// </summary>
    public uint PersistMode { get; internal set; }

    /// <summary>
    /// Gets the sources from the restore data, or <see langword="null"/> when there are none.
    /// </summary>
    public IReadOnlyList<ScreenCastSource>? RestoreCandidates { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the application called <c>SelectSources</c>.
    /// </summary>
    public bool SourcesSelected { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the streams were published.
    /// </summary>
    public bool Started { get; internal set; }

    /// <summary>
    /// Gets the published streams. The list is empty before the start and after the close.
    /// </summary>
    public IReadOnlyList<ScreenCastStream> Streams => _streams;

    internal List<ScreenCastStream> StreamList => _streams;
}
