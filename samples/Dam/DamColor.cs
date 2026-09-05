using Basin;
using Basin.Capabilities;
using Basin.Color;
using Basin.Desktop;
using Basin.Host;

using Basin.Diagnostics;

namespace Dam;

internal sealed partial class Dam
{
    private ColorManager? _color;
    private OutputColorDriver? _outputColor;
    private SurfaceLutDriver? _lutDriver;

    private void WireColor()
    {
        if (_services.Find<ColorManager>() is not { } color)
        {
            return;
        }

        _color = color;
        _outputColor = new OutputColorDriver(color, _colorPack.Configuration);
        _lutDriver = new SurfaceLutDriver(_scene, color, _colorPack.Luts);
        _lutDriver.CountChanged += attached =>
        {
            BasinReport.Line($"COLOR luts={attached}");
        };
        _lutDriver.WatchToplevels(_services.Require<Basin.Shell.Xdg.XdgShell>());
        _outputs.Added += DescribeOutput;
        _outputs.Removed += view => _outputColor.Remove(view.Global);
        foreach (var view in _outputs.Views)
        {
            DescribeOutput(view);
        }
    }

    private void DescribeOutput(OutputView view) => _outputColor?.Add(view.Global, view.Output, view.Scene);
}
