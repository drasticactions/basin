using Basin.Inspector;
using Basin.Scene;

namespace LunaWm;

internal sealed partial class LunaShell
{
    private InspectorSession? _inspector;

    internal InspectorInput? InspectorInput => _inspector?.Input;

    private void CreateInspector()
    {
        _inspector = new InspectorSession(new InspectorSessionOptions
        {
            Driver = _outputs,
            Scene = _scene,
            Below = _sessionLockLayer,
            Loop = _host.Loop,
            Settle = _settle,
            Seat = Seat,
            Services = _services,
            Renderer = _renderer,
            Allocator = _deviceAllocator,
            RenderNode = _renderer.Device?.DevicePath,
            Settings = _config.Inspector,
            DefaultOutput = () => _views.Count > 0 ? PlacementView().Driver : null,
            Report = _report,
        });
        _inspector.Changed += () =>
        {
            _seat.Refresh();
            _outputs.ScheduleAll();
        };
    }

    private void ToggleInspector()
    {
        if (_locked)
        {
            _report.Line("INSPECTOR unavailable the session is locked");
            return;
        }

        _inspector?.Toggle();
    }

    private void CloseInspector() => _inspector?.Close();
}
