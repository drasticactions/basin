namespace Basin;

public interface IHardwareCursor
{
    bool SetCursor(IBuffer? buffer, int hotspotX, int hotspotY);

    void MoveCursor(int x, int y);

    bool CursorAwaitingFrame => false;
}
