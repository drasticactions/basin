using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Color;

public sealed class ColorCapabilityPack : ICapabilityPack
{
    public ColorCapabilityPack(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Configuration = new ColorOutputConfiguration(new LayoutOutputConfiguration(layout));
    }

    public ColorOutputConfiguration Configuration { get; }

    public void Register(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Use<IOutputConfiguration>(Configuration);
    }
}
