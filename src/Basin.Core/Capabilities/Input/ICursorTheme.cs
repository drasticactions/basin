namespace Basin.Capabilities;

public interface ICursorTheme
{
    bool TryResolve(string shapeName, double scale, out CursorImage image);

    bool TryResolve(CursorShape shape, double scale, out CursorImage image) =>
        TryResolve(CursorShapeNames.NameOf(shape), scale, out image);
}
