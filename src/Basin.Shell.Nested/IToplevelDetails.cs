using Basin.Capabilities;

namespace Basin.Shell.Nested;

public interface IToplevelDetails
{
    bool TryGetDetails(out ToplevelInfo details);
}
