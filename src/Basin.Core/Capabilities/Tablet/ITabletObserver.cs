using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

public interface ITabletObserver
{
    void OnToolProximity(ulong toolId, ulong tabletId, bool inProximity);

    void OnToolAxis(ulong toolId, uint timeMs, TabletToolAxes axes);

    void OnToolButton(ulong toolId, uint timeMs, uint button, bool pressed);

    void OnPadEvent(ulong padId, uint timeMs, TabletPadEvent padEvent);
}
