using Basin.Capabilities;

namespace Basin;

public interface IShapedBackdropEffect : IBackdropEffect
{
    void SetShape(object key, in FrameBackdropShape shape);
}
