using Basin.Diagnostics;
using Basin.Host;
using Basin.Inspector;
using Basin.Ipc;
using Basin.Renderers;

namespace System7Wm;

internal sealed partial class System7Shell
{
    private InspectorSession? _inspector;
    private Basin.Seat.Backends.ChordMatcher? _inspectorChord;
    private Basin.Seat.Backends.ChordMatcher? _terminalChord;

    private void CreateInspector()
    {
        _inspector = new InspectorSession(new InspectorSessionOptions
        {
            Driver = _outputs,
            Scene = _scene,
            Layer = _layers.Inspector,
            Loop = _host.Loop,
            Settle = _settle,
            Seat = Seat,
            Services = _services,
            Renderer = _renderer,
            Allocator = _deviceAllocator,
            RenderNode = RendererCatalog.FindRenderNode(),
            Settings = _config.Inspector,
            PointerPosition = () => (_seat.PointerX, _seat.PointerY),
            Report = _report,
        });
        _seat.Inspector = _inspector.Input;
        _inspector.Changed += _seat.Refresh;
    }

    private void ApplyChords()
    {
        _inspectorChord ??= new Basin.Seat.Backends.ChordMatcher(Seat.Keyboard);
        _terminalChord ??= new Basin.Seat.Backends.ChordMatcher(Seat.Keyboard);
        Apply(_inspectorChord, _config.InspectorChord);
        Apply(_terminalChord, _config.NewTerminal);

        static void Apply(Basin.Seat.Backends.ChordMatcher matcher, Basin.Config.Hotkey? hotkey)
        {
            if (hotkey is null)
            {
                matcher.Clear();
            }
            else
            {
                matcher.Set(hotkey.Keysym, hotkey.ModifierMask);
            }
        }
    }

    private bool TryInspectorChord(uint key)
    {
        if (_inspector is null || _inspectorChord?.MatchesKeysym(Seat.Keyboard.RawKeysymFor(key)) != true)
        {
            return false;
        }

        BasinReport.Line("CHORD inspector");
        _inspector.Toggle();
        return true;
    }

    private void RegisterInspectorCommands(IpcMethodRegistry methods) => _inspector?.Register(methods);

    private void DisposeInspector()
    {
        if (_inspector is { } inspector)
        {
            inspector.Changed -= _seat.Refresh;
            _seat.Inspector = null;
            inspector.Dispose();
        }

        _inspector = null;
    }
}
