using System.Diagnostics.CodeAnalysis;
using Basin;
using Basin.Backend.Headless;
using Basin.Capabilities;
using Basin.Host;

namespace PlasmaHost;

internal sealed class PlasmaHostVirtualOutputs(HeadlessBackend backend) : IVirtualOutputFactory
{
    private readonly HeadlessVirtualOutputFactory _factory = new(backend);

    public OutputDriver? Outputs { get; set; }

    public bool TryCreate(string name, string description, int width, int height,
                          double scale, [NotNullWhen(true)] out IOutput? output)
    {
        if (Outputs is null || !_factory.TryCreate(name, description, width, height, scale, out output))
        {
            output = null;
            return false;
        }

        Outputs.AddView(output);
        return true;
    }

    public void Destroy(IOutput output)
    {
        if (Outputs?.ViewOf(output) is { } view)
        {
            Outputs.RemoveView(view);
        }

        _factory.Destroy(output);
    }
}
