using Basin.Capabilities;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

public sealed class SurfaceLutDriver
{
    private readonly Scene.Scene _scene;
    private readonly Func<Surface, IColorLut?> _resolve;
    private int _lastCount = -1;

    public SurfaceLutDriver(Scene.Scene scene, ColorManager color, Func<Surface, IColorLut?> resolve)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(color);
        ArgumentNullException.ThrowIfNull(resolve);
        _scene = scene;
        _resolve = resolve;
        color.SurfaceDescriptionChanged += (_, _) => Refresh();
    }

    public event Action<int>? CountChanged;

    public static void DeclareSrgb(ColorManager color)
    {
        ArgumentNullException.ThrowIfNull(color);
        color.SupportedTransferFunctions =
            [ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22, ColorTransferFunction.ExtLinear];
        color.SupportedPrimaries = [ColorPrimaries.Srgb];
    }

    public void WatchToplevels(XdgShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.NewToplevel += toplevel => toplevel.Xdg.Mapped += Refresh;
    }

    public void Refresh()
    {
        var attached = _scene.AttachLuts(_resolve);
        if (attached != _lastCount)
        {
            _lastCount = attached;
            CountChanged?.Invoke(attached);
        }
    }
}
