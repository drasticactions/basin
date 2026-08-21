using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

public interface IToplevelObserver
{
    void OnToplevelAdded(ulong toplevelId);

    void OnToplevelChanged(ulong toplevelId);

    void OnToplevelRemoved(ulong toplevelId);
}
