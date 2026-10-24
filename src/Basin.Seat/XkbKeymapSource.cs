using Basin.Capabilities;
using Wayland;
using Xkb;

namespace Basin.Seat;

/// <summary>
/// The default <see cref="IKeymapSource"/>. It compiles keymaps with libxkbcommon and keeps the last compiled keymap for keysym translation.
/// </summary>
public sealed class XkbKeymapSource : IKeymapSource, IDisposable
{
    private readonly Wayland.Server.Shm.IShmBlobFactory? _blobs;
    private XkbContext? _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="XkbKeymapSource"/> class.
    /// </summary>
    /// <param name="blobs">The factory for the shared memory that carries the keymap text to clients, or <see langword="null"/> for the default.</param>
    public XkbKeymapSource(Wayland.Server.Shm.IShmBlobFactory? blobs = null) => _blobs = blobs;

    /// <summary>
    /// Gets the keymap of the last successful compile. The source owns it and disposes it on the next successful compile.
    /// </summary>
    public XkbKeymap? LastCompiled { get; private set; }

    /// <inheritdoc/>
    public bool TryCompile(string keymapText, out Keymap keymap)
    {
        ArgumentNullException.ThrowIfNull(keymapText);
        _context ??= XkbContext.Create();
        XkbKeymap? compiled;
        try
        {
            compiled = _context.CreateKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(keymapText));
        }
        catch (XkbException)
        {
            compiled = null;
        }

        return Finish(compiled, out keymap);
    }

    /// <inheritdoc/>
    public bool TryCompile(in KeymapNames names, out Keymap keymap)
    {
        _context ??= XkbContext.Create();
        XkbKeymap? compiled;
        try
        {
            compiled = _context.CreateKeymap(new XkbRuleNames
            {
                Rules = names.Rules,
                Model = names.Model,
                Layout = names.Layout,
                Variant = names.Variant,
                Options = names.Options,
            });
        }
        catch (XkbException)
        {
            compiled = null;
        }

        return Finish(compiled, out keymap);
    }

    /// <summary>
    /// Disposes <see cref="LastCompiled"/> and the xkb context. A later compile creates a new context.
    /// </summary>
    public void Dispose()
    {
        LastCompiled?.Dispose();
        LastCompiled = null;
        _context?.Dispose();
        _context = null;
    }

    private bool Finish(XkbKeymap? compiled, out Keymap keymap)
    {
        if (compiled is null)
        {
            keymap = null!;
            return false;
        }

        LastCompiled?.Dispose();
        LastCompiled = compiled;
        keymap = new Keymap(compiled.AsString(), _blobs);
        return true;
    }
}
