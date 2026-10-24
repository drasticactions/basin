namespace Basin.Rashader;

/// <summary>
/// A frame filter that runs one or more librashader preset chains. The GL and Vulkan filters both
/// implement it, so a consumer holds one type for either renderer.
/// </summary>
public interface IRashaderFilter : IFrameFilter, IDisposable
{
    /// <summary>
    /// Gets the runtime parameters that the preset declares, in preset order. The list does not change
    /// after creation.
    /// </summary>
    IReadOnlyList<RashaderParameter> Parameters { get; }

    /// <summary>
    /// Sets a runtime parameter of the preset by name. The call does not allocate.
    /// </summary>
    /// <param name="name">The parameter name as the preset declares it.</param>
    /// <param name="value">The new value. librashader applies its own range checks.</param>
    /// <returns><see langword="false"/> when the chain is gone, the preset has no parameter with that name, or librashader refuses the value.</returns>
    bool TrySetParameter(string name, float value);
}
