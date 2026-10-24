using Basin.Capabilities;
using Xkb;

namespace Basin.Seat;

/// <summary>
/// Compiles the keymap of the host layout, with the embedded US keymap as the fallback.
/// It reads the layout through an <see cref="IHostKeyboardLayout"/>.
/// </summary>
public sealed class HostKeymapSource : IKeymapSource, IDisposable
{
    private readonly XkbKeymapSource _xkb;
    private readonly IHostKeyboardLayout? _layout;
    private readonly bool _ownsLayout;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostKeymapSource"/> class.
    /// </summary>
    /// <param name="layout">The layout reader, or <see langword="null"/> to detect one with <see cref="HostKeyboardLayout.Detect"/>. The source disposes only a reader that it detected.</param>
    /// <param name="blobs">The factory for the shared-memory keymap blobs, or <see langword="null"/> for the default.</param>
    public HostKeymapSource(
        IHostKeyboardLayout? layout = null,
        Wayland.Server.Shm.IShmBlobFactory? blobs = null)
    {
        _ownsLayout = layout is null;
        _layout = layout ?? HostKeyboardLayout.Detect();
        _xkb = new XkbKeymapSource(blobs);
        if (_layout is not null)
        {
            _layout.Changed += OnChanged;
        }
    }

    /// <summary>
    /// Gets the name of the host layout, or <c>us</c> when there is no reader.
    /// </summary>
    public string LayoutName => _layout?.Name ?? "us";

    /// <summary>
    /// Gets the XKB keymap of the last successful compile, or <see langword="null"/>.
    /// The source owns it and disposes it at the next successful compile.
    /// </summary>
    public XkbKeymap? LastCompiled => _xkb.LastCompiled;

    /// <summary>
    /// Gets a value indicating whether the last <see cref="TryCompile(out Keymap)"/> call compiled the host layout and not the fallback.
    /// </summary>
    public bool ReadFromHost { get; private set; }

    /// <summary>
    /// Occurs when the reader reports a layout change.
    /// It occurs on the thread of the reader, which can be a thread other than the compositor thread.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Compiles the host layout. When there is no reader, or the read or the compile fails, it compiles the embedded US keymap.
    /// </summary>
    /// <param name="keymap">The compiled keymap. The caller owns it and must dispose it.</param>
    /// <returns><see langword="false"/> only when the fallback also does not compile.</returns>
    public bool TryCompile(out Keymap keymap)
    {
        if (_layout is not null && _layout.TryReadKeymapText(out var text) && _xkb.TryCompile(text, out keymap))
        {
            ReadFromHost = true;
            return true;
        }

        ReadFromHost = false;
        return _xkb.TryCompile(HostKeymapWriter.Fallback, out keymap);
    }

    /// <inheritdoc/>
    public bool TryCompile(string keymapText, out Keymap keymap) => _xkb.TryCompile(keymapText, out keymap);

    /// <inheritdoc/>
    public bool TryCompile(in KeymapNames names, out Keymap keymap) => _xkb.TryCompile(names, out keymap);

    /// <summary>
    /// Stops listening to the reader, disposes a reader that the source detected, and releases the XKB state.
    /// </summary>
    public void Dispose()
    {
        if (_layout is not null)
        {
            _layout.Changed -= OnChanged;
            if (_ownsLayout && _layout is IDisposable owned)
            {
                owned.Dispose();
            }
        }

        _xkb.Dispose();
    }

    private void OnChanged() => Changed?.Invoke();
}
