namespace Basin;

[Flags]
public enum BufferUse
{
    Render = 1,
    Scanout = 2,
    Cursor = 4,
}
