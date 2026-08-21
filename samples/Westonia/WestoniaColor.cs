using Basin;
using Basin.Capabilities;
using Basin.Color;
using Basin.Desktop;
using Basin.Host;
using Microsoft.Extensions.Logging;

namespace Westonia;

internal sealed partial class Westonia
{
    private static readonly ImageDescription OutputDescription = ImageDescription.Srgb;

    private ColorManager? _color;
    private ColorLutCache? _luts;
    private Func<Surface, IColorLut?>? _resolveLut;
    private int _lastLutCount = -1;

    private void WireColor()
    {
        _color = _services.Find<ColorManager>();
        if (_color is null)
        {
            _log.LogInformation("no colour manager: this compositor offers no colour management");
            return;
        }

        _color.SupportedTransferFunctions =
            [ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22, ColorTransferFunction.ExtLinear];
        _color.SupportedPrimaries = [ColorPrimaries.Srgb];

        _luts = new ColorLutCache(_renderer);
        _resolveLut = surface => _luts.LutFor(_color.DescriptionOf(surface), OutputDescription);
        _color.SurfaceDescriptionChanged += (_, _) => RefreshSurfaceLuts();
        _color.OutputDescriptionChanged += (global, description) => _cursor.Describe(global.Output, description);
        Shell.NewToplevel += toplevel => toplevel.Xdg.Mapped += RefreshSurfaceLuts;
    }

    private void DescribeOutput(OutputView view) =>
        _color?.SetOutputDescription(view.Global, OutputDescription);

    internal void RefreshSurfaceLuts()
    {
        if (_resolveLut is null)
        {
            return;
        }

        var attached = _scene.AttachLuts(_resolveLut);
        if (attached == _lastLutCount)
        {
            return;
        }

        _lastLutCount = attached;
        Console.WriteLine($"COLOR luts={attached}");
        Console.Out.Flush();
    }
}
