using Basin.Capabilities;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

public sealed class SurfaceLutDriver
{
    private readonly Scene.Scene _scene;
    private readonly ColorManager _color;
    private readonly Func<Surface, ImageDescription?> _describe;
    private int _lastCount = -1;

    public SurfaceLutDriver(Scene.Scene scene, ColorManager color, IColorTransformResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(color);
        ArgumentNullException.ThrowIfNull(resolver);
        _scene = scene;
        _color = color;
        _describe = Describe;
        scene.ColorTransforms = resolver;
        color.SurfaceDescriptionChanged += (_, _) => Refresh();
        color.OutputDescriptionChanged += (_, _) => Refresh();
        color.OutputDescriptionRemoved += _ => Refresh();
    }

    public event Action<int>? CountChanged;

    public static void DeclareSrgb(ColorManager color)
    {
        ArgumentNullException.ThrowIfNull(color);
        color.SupportedTransferFunctions =
        [
            ColorTransferFunction.CompoundPower24, ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22,
            ColorTransferFunction.ExtLinear,
        ];
        color.SupportedPrimaries = [ColorPrimaries.Srgb];
    }

    public static void Declare(ColorManager color, IEnumerable<ImageDescription> outputs)
    {
        ArgumentNullException.ThrowIfNull(color);
        ArgumentNullException.ThrowIfNull(outputs);

        var transfers = new List<ColorTransferFunction>
        {
            ColorTransferFunction.CompoundPower24, ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22,
            ColorTransferFunction.ExtLinear,
        };
        var primaries = new List<ColorPrimaries> { ColorPrimaries.Srgb };

        foreach (var description in outputs)
        {
            if (description.TransferNamed is { } transfer && !transfers.Contains(transfer))
            {
                transfers.Add(transfer);
            }

            if (description.PrimariesNamed is { } named && !primaries.Contains(named))
            {
                primaries.Add(named);
            }
        }

        color.SupportedTransferFunctions = transfers;
        color.SupportedPrimaries = primaries;
    }

    public void WatchToplevels(XdgShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.NewToplevel += toplevel => toplevel.Xdg.Mapped += Refresh;
    }

    public void Refresh()
    {
        _scene.DescribeSurfaces(_describe);
        var attached = _scene.LutCount;
        if (attached != _lastCount)
        {
            _lastCount = attached;
            CountChanged?.Invoke(attached);
        }
    }

    private ImageDescription? Describe(Surface surface)
    {
        var description = _color.DescriptionOf(surface);
        return ReferenceEquals(description, ImageDescription.SdrDefault) ? null : description;
    }
}
