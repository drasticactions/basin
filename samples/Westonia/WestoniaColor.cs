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
    private SurfaceLutDriver? _lutDriver;

    private void WireColor()
    {
        _color = _services.Find<ColorManager>();
        if (_color is null)
        {
            _log.LogInformation("no colour manager: this compositor offers no colour management");
            return;
        }

        SurfaceLutDriver.DeclareSrgb(_color);

        _luts = new ColorLutCache(_renderer);
        _lutDriver = new SurfaceLutDriver(
            _scene, _color, surface => _luts.LutFor(_color.DescriptionOf(surface), OutputDescription));
        _lutDriver.CountChanged += attached =>
        {
            Console.WriteLine($"COLOR luts={attached}");
            Console.Out.Flush();
        };
        _color.OutputDescriptionChanged += (global, description) => _cursor.Describe(global.Output, description);
        _lutDriver.WatchToplevels(Shell);
    }

    private void DescribeOutput(OutputView view) =>
        _color?.SetOutputDescription(view.Global, OutputDescription);

    internal void RefreshSurfaceLuts() => _lutDriver?.Refresh();
}
