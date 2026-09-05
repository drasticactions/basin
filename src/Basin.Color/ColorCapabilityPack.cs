using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Color;

public sealed class ColorCapabilityPack : ICapabilityPack
{
    public ColorCapabilityPack(OutputLayout layout, IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(renderer);
        Layout = new LayoutOutputConfiguration(layout);
        Configuration = new ColorOutputConfiguration(Layout);
        Luts = new ColorLutCache(renderer);
    }

    public ColorOutputConfiguration Configuration { get; }

    public LayoutOutputConfiguration Layout { get; }

    public ColorLutCache Luts { get; }

    public void Register(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Use<IOutputConfiguration>(Configuration);
        services.UseDefault<IColorTransformResolver>(Luts);
    }
}
