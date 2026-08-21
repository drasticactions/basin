using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

public static class CorePack
{
    public static ProtocolPack Default => new(
    [
        new ShmModule(),
        new CompositorModule(),
        new SubcompositorModule(),
        new FixesModule(),
        new ViewporterModule(),
        new PresentationTimeModule(),
    ]);
}
