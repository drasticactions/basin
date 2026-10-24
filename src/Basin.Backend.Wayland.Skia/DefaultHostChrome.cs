using Basin.UI.Skia;

namespace Basin.Backend.Wayland.Skia;

/// <summary>
/// The default chrome for a nested output: a <see cref="FrameHostChrome"/> that draws the flat frame on a raster <see cref="SkiaUIHost"/>.
/// It owns the host, the theme and the chrome.
/// </summary>
public sealed class DefaultHostChrome : IDisposable
{
    private readonly WaylandOutput _output;
    private readonly SkiaUIHost _host;
    private readonly FlatFrameTheme _theme;
    private bool _disposed;

    private DefaultHostChrome(WaylandOutput output, WaylandHostFrame frame, string? title)
    {
        _output = output;
        _host = new SkiaUIHost();
        _theme = FlatFrameTheme.Default();
        Chrome = new FrameHostChrome(frame, _host, new FlatFrameRenderer(_theme), title);
        _output.Committed += Chrome.OnOutputChanged;
    }

    /// <summary>Gets the chrome that draws the frame and handles its input.</summary>
    public FrameHostChrome Chrome { get; }

    /// <summary>
    /// Draws the default chrome on <paramref name="frame"/> now and lays it out again on each commit of <paramref name="output"/>.
    /// </summary>
    /// <param name="output">The output that owns <paramref name="frame"/>.</param>
    /// <param name="frame">The host frame to draw.</param>
    /// <param name="title">A title that replaces the output's own title, or <see langword="null"/>.</param>
    /// <returns>The chrome. The caller disposes it before the output.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> or <paramref name="frame"/> is <see langword="null"/>.</exception>
    public static DefaultHostChrome Attach(WaylandOutput output, WaylandHostFrame frame, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(frame);
        return new DefaultHostChrome(output, frame, title);
    }

    /// <summary>
    /// Attaches the default chrome to each host frame that <paramref name="output"/> makes available, now or later.
    /// </summary>
    /// <param name="output">The output to frame.</param>
    /// <param name="title">A title that replaces the output's own title, or <see langword="null"/>.</param>
    /// <returns>A handle that unsubscribes and disposes the chrome. Under a parent that decorates, no chrome is ever made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static IDisposable Wire(WaylandOutput output, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new Wiring(output, title);
    }

    /// <summary>Unsubscribes from the output and disposes the chrome, the theme and the UI host. A second call does nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _output.Committed -= Chrome.OnOutputChanged;
        Chrome.Dispose();
        _theme.Dispose();
        _host.Dispose();
    }

    private sealed class Wiring : IDisposable
    {
        private readonly WaylandOutput _output;
        private readonly string? _title;
        private DefaultHostChrome? _chrome;

        public Wiring(WaylandOutput output, string? title)
        {
            _output = output;
            _title = title;
            output.HostFrameAvailable += OnFrame;
        }

        public void Dispose()
        {
            _output.HostFrameAvailable -= OnFrame;
            _chrome?.Dispose();
            _chrome = null;
        }

        private void OnFrame(WaylandHostFrame frame)
        {
            _chrome?.Dispose();
            _chrome = Attach(_output, frame, _title);
        }
    }
}
