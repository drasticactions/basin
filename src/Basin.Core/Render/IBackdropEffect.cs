using Pixman;

namespace Basin;

public interface IBackdropEffect
{
    bool ForgetSurface(object key) => false;
}
