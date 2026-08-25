using Basin;
using Basin.Capabilities;
using Basin.Color;
using Basin.Desktop;
using Basin.Host;

using Basin.Diagnostics;

namespace Westonia;

internal sealed partial class Westonia
{
    private ColorManager? _color;
    private ColorLutCache? _luts;
    private SurfaceLutDriver? _lutDriver;

    private void WireColor()
    {
        _color = _services.Find<ColorManager>();
        if (_color is null)
        {
            _log.Info($"no colour manager: this compositor offers no colour management");
            return;
        }

        DeclareColor();

        _luts = new ColorLutCache(_renderer);
        _lutDriver = new SurfaceLutDriver(
            _scene, _color, surface => _luts.LutFor(_color.DescriptionOf(surface), PrimaryDescription()));
        _lutDriver.CountChanged += attached =>
        {
            BasinReport.Line($"COLOR luts={attached}");
        };
        _color.OutputDescriptionChanged += (global, description) => _cursor.Describe(global.Output, description);
        _lutDriver.WatchToplevels(Shell);
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

    private void DescribeOutput(OutputView view)
    {
        _color?.SetOutputDescription(view.Global, DescriptionOf(view.Output));
        DeclareColor();
    }

    internal void RefreshSurfaceLuts() => _lutDriver?.Refresh();
}
