using System.Diagnostics.CodeAnalysis;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Color;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

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

    public ColorManager? Manager { get; private set; }

    public OutputColorDriver? Outputs { get; private set; }

    public SurfaceLutDriver? Surfaces { get; private set; }

    public void Register(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Use<IOutputConfiguration>(Configuration);
        services.UseDefault<IColorTransformResolver>(Luts);
        services.UseDefault<IOutputLooks>(Configuration);
    }

    [MemberNotNullWhen(true, nameof(Manager), nameof(Outputs), nameof(Surfaces))]
    public bool Attach(BasinServices services, Scene.Scene scene)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scene);
        if (Manager is not null)
        {
            throw new InvalidOperationException("the color drivers are already attached");
        }

        if (services.Find<ColorManager>() is not { } color)
        {
            return false;
        }

        Manager = color;
        Outputs = new OutputColorDriver(color, Configuration);
        Surfaces = new SurfaceLutDriver(scene, color, Luts);
        if (services.Find<XdgShell>() is { } shell)
        {
            Surfaces.WatchToplevels(shell);
        }

        return true;
    }

    public void AddOutput(OutputGlobal global, IOutput output, SceneOutput? scene) =>
        Outputs?.Add(global, output, scene);

    public void RemoveOutput(OutputGlobal global) => Outputs?.Remove(global);

    public void RefreshSurfaces() => Surfaces?.Refresh();
}
