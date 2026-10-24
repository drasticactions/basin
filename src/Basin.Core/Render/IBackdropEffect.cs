using Pixman;

namespace Basin;

/// <summary>
/// Marks an effect that transforms the pixels already drawn under a region. Each renderer defines the concrete interface it accepts, and a foreign effect throws.
/// </summary>
public interface IBackdropEffect
{
    /// <summary>
    /// Drops the state that the effect keeps for one key, for example a shape or a cached level.
    /// </summary>
    /// <param name="key">The key that the draws used.</param>
    /// <returns><see langword="true"/> when the effect had state for the key. The default keeps no state and returns <see langword="false"/>.</returns>
    bool ForgetSurface(object key) => false;
}
