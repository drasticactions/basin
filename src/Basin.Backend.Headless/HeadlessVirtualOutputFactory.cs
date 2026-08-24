using System.Diagnostics.CodeAnalysis;
using Basin.Capabilities;

namespace Basin.Backend.Headless;

public sealed class HeadlessVirtualOutputFactory(HeadlessBackend backend) : IVirtualOutputFactory
{
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

    public void Destroy(IOutput output) => (output as HeadlessOutput)?.Destroy();
}
