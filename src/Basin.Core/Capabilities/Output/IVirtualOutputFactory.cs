using System.Diagnostics.CodeAnalysis;

namespace Basin.Capabilities;

public interface IVirtualOutputFactory
{
    bool TryCreate(string name, string description, int width, int height,
                   double scale, [NotNullWhen(true)] out IOutput? output);

    void Destroy(IOutput output);
}
