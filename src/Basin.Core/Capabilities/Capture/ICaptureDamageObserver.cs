using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

public interface ICaptureDamageObserver
{
    void OnSourceDamaged(IOutput output, Box damage);
}
