using System.Diagnostics.CodeAnalysis;
using Pixman;

namespace Basin;

public interface IHardwareCursor
{
    bool SetCursor(IBuffer? buffer, int hotspotX, int hotspotY);

    void MoveCursor(int x, int y);

    bool CursorAwaitingFrame => false;

    bool TryPresentedCursor([NotNullWhen(true)] out IBuffer? buffer, out Box destination)
    {
        buffer = null;
        destination = default;
        return false;
    }
}
