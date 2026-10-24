using System.Diagnostics.CodeAnalysis;
using Basin.Capabilities;

namespace Basin.Backend.Headless;

/// <summary>
/// Creates virtual outputs as <see cref="HeadlessOutput"/> instances on a <see cref="HeadlessBackend"/>.
/// </summary>
/// <param name="backend">The backend that creates and owns the outputs.</param>
public sealed class HeadlessVirtualOutputFactory(HeadlessBackend backend) : IVirtualOutputFactory
{
    /// <summary>
    /// Creates an enabled 60 Hz headless output with the given size and scale. The description is not used.
    /// </summary>
    /// <param name="name">The output name.</param>
    /// <param name="description">Not used.</param>
    /// <param name="width">The width in physical pixels.</param>
    /// <param name="height">The height in physical pixels.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="output">The new output, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="false"/> when the width, height or scale is not positive.</returns>
    public bool TryCreate(string name, string description, int width, int height,
                          double scale, [NotNullWhen(true)] out IOutput? output)
    {
        output = null;
        if (width <= 0 || height <= 0 || scale <= 0)
        {
            return false;
        }

        var created = backend.CreateOutput(new OutputMode(width, height, 60_000), name: name);
        using var state = new OutputState();
        created.Commit(state.SetScale(scale));
        output = created;
        return true;
    }

    /// <summary>
    /// Destroys the output if it is a <see cref="HeadlessOutput"/>. Other outputs are ignored.
    /// </summary>
    /// <param name="output">The output to destroy.</param>
    public void Destroy(IOutput output) => (output as HeadlessOutput)?.Destroy();
}
