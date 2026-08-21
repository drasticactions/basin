namespace Basin;

public interface IParentCursor
{
    bool SetCursor(IBuffer image, int hotspotX, int hotspotY, double scale = 1.0);

    void HideCursor();
}
