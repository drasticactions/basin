using Basin;
using Basin.Capabilities;
using Basin.Color;
using Basin.Desktop;
using Basin.Host;

using Basin.Diagnostics;

namespace PlasmaHost;

internal sealed partial class PlasmaHost
{
    private ColorManager? _color;
    private ColorLutCache? _luts;
    private SurfaceLutDriver? _lutDriver;

    private void WireColor()
    {
        if (_services.Find<ColorManager>() is not { } color)
        {
            return;
        }

        _color = color;
        _luts = new ColorLutCache(_renderer);
        DeclareColor();
        _lutDriver = new SurfaceLutDriver(
            _scene, color, surface => _luts.LutFor(color.DescriptionOf(surface), PrimaryDescription()));
        _lutDriver.CountChanged += attached =>
        {
            BasinReport.Line($"COLOR luts={attached}");
        };
        _lutDriver.WatchToplevels(_services.Require<Basin.Shell.Xdg.XdgShell>());
        _outputs.Added += DescribeOutput;
        foreach (var view in _outputs.Views)
        {
            DescribeOutput(view);
        }
    }

    private ImageDescription DescriptionOf(IOutput output) => _colorPack.Configuration.DescriptionOf(output);

    private ImageDescription PrimaryDescription() =>
        _outputs.Views.Count > 0 ? DescriptionOf(_outputs.Views[0].Output) : ImageDescription.Srgb;

    private void DeclareColor()
    {
        if (_color is { } color)
        {
            SurfaceLutDriver.Declare(color, _outputs.Views.Select(v => DescriptionOf(v.Output)));
        }
    }

    private void RedescribeOutputs()
    {
        foreach (var view in _outputs.Views)
        {
            DescribeOutput(view);
        }
    }

    private void DescribeOutput(OutputView view)
    {
        _color?.SetOutputDescription(view.Global, DescriptionOf(view.Output));
        DeclareColor();
        _lutDriver?.Refresh();
    }
}
