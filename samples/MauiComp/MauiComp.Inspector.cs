using Basin;
using Basin.Host;
using Basin.Inspector;
using Basin.Renderers;
using Xkb;

namespace MauiComp;

internal sealed partial class MauiComp
{
    private Basin.Seat.Backends.ChordMatcher? _inspectorChord;

    private InspectorSession? _inspector;

    private void WireInspector()
    {
        _inspectorChord = new Basin.Seat.Backends.ChordMatcher(Seat.Keyboard);
        _inspectorChord.Set(Basin.Config.Keysym.FromName("F12"), Basin.Config.Modifiers.Super);
        _inspector = new InspectorSession(new InspectorSessionOptions
        {
            Driver = _outputs,
            Scene = _scene,
            Layer = _layers.Overlay,
            Loop = _host.Loop,
            Settle = _settle,
            Seat = Seat,
            Services = _services,
            Renderer = _renderer,
            Allocator = _deviceAllocator,
            RenderNode = RendererCatalog.FindRenderNode(),
            Report = _report,
            Settings = _config.Inspector,
            PointerPosition = () => _seat is { } seat ? (seat.PointerX, seat.PointerY) : (0, 0),
        });
        _inspector.Changed += () => _seat?.RefreshPointer();
        if (_seat is not null)
        {
            _seat.Inspector = _inspector.Input;
        }
    }

    private bool InspectorBinding(XkbKeysym symbol, bool pressed)
    {
        if (_inspectorChord?.MatchesKeysym(symbol) != true)
        {
            return false;
        }

        if (pressed)
        {
            _inspector?.Toggle();
        }

        return true;
    }
}
