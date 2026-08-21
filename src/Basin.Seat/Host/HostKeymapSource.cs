using Basin.Capabilities;
using Xkb;

namespace Basin.Seat;

public sealed class HostKeymapSource : IKeymapSource, IDisposable
{
    private readonly XkbKeymapSource _xkb;
    private readonly IHostKeyboardLayout? _layout;
    private readonly bool _ownsLayout;

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

    public string LayoutName => _layout?.Name ?? "us";

    public XkbKeymap? LastCompiled => _xkb.LastCompiled;

    public bool ReadFromHost { get; private set; }

    public event Action? Changed;

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

    public bool TryCompile(string keymapText, out Keymap keymap) => _xkb.TryCompile(keymapText, out keymap);

    public bool TryCompile(in KeymapNames names, out Keymap keymap) => _xkb.TryCompile(names, out keymap);

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
