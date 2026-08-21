using Basin;
using Basin.Capabilities;
using Basin.Color;
using Basin.Desktop;

namespace EightWm;

internal sealed partial class Shell
{
    private ColorManager? _color;
    private ColorLutCache? _luts;
    private ImageDescription _outputDescription = ImageDescription.Srgb;
    private Func<Surface, IColorLut?>? _resolveLut;
    private int _lutCount = -1;

    private void AttachColor()
    {
        if (_services.Find<ColorManager>() is not { } color)
        {
            return;
        }

        _color = color;
        _luts = new ColorLutCache(_renderer);
        color.SupportedTransferFunctions =
            [ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22, ColorTransferFunction.ExtLinear];
        color.SupportedPrimaries = [ColorPrimaries.Srgb];
        color.SurfaceDescriptionChanged += (_, _) => RefreshLuts();
        color.OutputDescriptionChanged += (global, description) =>
            _seat.DescribeCursor(global.Output, description);

        Shells.NewToplevel += window => window.Xdg.Mapped += RefreshLuts;
        _outputs.Added += driver => DescribeOutput(ViewOf(driver));
        _outputs.LayoutChanged += RefreshLuts;
        foreach (var view in Views)
        {
            DescribeOutput(view);
        }
    }

    private void DescribeOutput(ShellView view)
    {
        _color?.SetOutputDescription(view.Global, _outputDescription);
        RefreshLuts();
    }

    private void RefreshLuts()
    {
        if (_color is not { } color || _luts is not { } luts)
        {
            return;
        }

        _resolveLut ??= surface => luts.LutFor(color.DescriptionOf(surface), _outputDescription);
        var attached = _scene.AttachLuts(_resolveLut);
        if (attached == _lutCount)
        {
            return;
        }

        _lutCount = attached;
        Console.WriteLine($"COLOR luts={attached}");
    }
}
