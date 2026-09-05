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
    private OutputColorDriver? _outputColor;
    private SurfaceLutDriver? _lutDriver;

    private void WireColor()
    {
        _color = _services.Find<ColorManager>();
        if (_color is null)
        {
            _log.Info($"no colour manager: this compositor offers no colour management");
            return;
        }

        _outputColor = new OutputColorDriver(_color, _colorPack.Configuration);
        _lutDriver = new SurfaceLutDriver(_scene, _color, _colorPack.Luts);
        _lutDriver.CountChanged += attached =>
        {
            BasinReport.Line($"COLOR luts={attached}");
        };
        _color.OutputDescriptionChanged += (global, description) => _cursor.Describe(global.Output, description);
        _lutDriver.WatchToplevels(Shell);
    }

    private void DescribeOutput(OutputView view) => _outputColor?.Add(view.Global, view.Output, view.Scene);

    private void ForgetOutput(OutputView view) => _outputColor?.Remove(view.Global);

    internal void RefreshSurfaceLuts() => _lutDriver?.Refresh();
}
