using Basin.Capabilities;
using Wayland;
using Xkb;

namespace Basin.Seat;

public sealed class XkbKeymapSource : IKeymapSource, IDisposable
{
    private readonly Wayland.Server.Shm.IShmBlobFactory? _blobs;
    private XkbContext? _context;

    public XkbKeymapSource(Wayland.Server.Shm.IShmBlobFactory? blobs = null) => _blobs = blobs;

    public XkbKeymap? LastCompiled { get; private set; }

    public bool TryCompile(string keymapText, out Keymap keymap)
    {
        ArgumentNullException.ThrowIfNull(keymapText);
        _context ??= XkbContext.Create();
        return Finish(_context.CreateKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(keymapText)), out keymap);
    }

    public bool TryCompile(in KeymapNames names, out Keymap keymap)
    {
        _context ??= XkbContext.Create();
        return Finish(
            _context.CreateKeymap(new XkbRuleNames
            {
                Rules = names.Rules,
                Model = names.Model,
                Layout = names.Layout,
                Variant = names.Variant,
                Options = names.Options,
            }),
            out keymap);
    }

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
