using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Color;

public sealed class ColorCapabilityPack : ICapabilityPack
{
    public ColorCapabilityPack(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Layout = new LayoutOutputConfiguration(layout);
        Configuration = new ColorOutputConfiguration(Layout);
    }

    public ColorOutputConfiguration Configuration { get; }

    public LayoutOutputConfiguration Layout { get; }

    public void Register(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Use<IOutputConfiguration>(Configuration);
    }
}
