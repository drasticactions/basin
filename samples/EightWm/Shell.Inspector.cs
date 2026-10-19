using Basin.Host;
using Basin.Inspector;
using Basin.Scene;
using Xkb;

namespace EightWm;

internal sealed partial class Shell
{
    private InspectorSession? _inspector;
    private Basin.Seat.Backends.ChordMatcher? _inspectorChord;

    internal InspectorSession? Inspector => _inspector;

    private void CreateInspector()
    {
        _inspector = new InspectorSession(new InspectorSessionOptions
        {
            Driver = _outputs,
            Scene = _scene,
            Below = _ownLockLayer,
            Loop = _host.Loop,
            Settle = _settle,
            Seat = Seat,
            Services = _services,
            Renderer = _renderer,
            Allocator = _deviceAllocator,
            RenderNode = Basin.Renderers.RendererCatalog.FindRenderNode(),
            Settings = _config.Inspector,
            PointerPosition = () => (_seat.PointerX, _seat.PointerY),
            Report = _report,
        });
        _inspector.Changed += () => _seat.Refocus();
        _seat.TouchChrome.First = _inspector.Input;
        ReadInspectorChord();
    }

    private void ReadInspectorChord()
    {
        _inspectorChord ??= new Basin.Seat.Backends.ChordMatcher(Seat.Keyboard);
        _inspectorChord.TrySet(_config.InspectorChord, _log);
    }

    private bool IsInspectorChord(XkbKeysym symbol) => _inspectorChord?.MatchesKeysym(symbol) == true;

    private void ReloadInspector()
    {
        ReadInspectorChord();
        _inspector?.Apply(_config.Inspector);
    }

    private void DisposeInspector()
    {
        _inspector?.Dispose();
        _inspector = null;
    }
}
