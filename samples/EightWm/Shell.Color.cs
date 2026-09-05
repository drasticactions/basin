using Basin;
using Basin.Capabilities;
using Basin.Color;
using Basin.Desktop;

using Basin.Diagnostics;

namespace EightWm;

internal sealed partial class Shell
{
    private ColorManager? _color;
    private OutputColorDriver? _outputColor;
    private SurfaceLutDriver? _lutDriver;

    private void AttachColor()
    {
        if (_services.Find<ColorManager>() is not { } color)
        {
            return;
        }

        _color = color;
        _outputColor = new OutputColorDriver(color, _colorPack.Configuration);
        _lutDriver = new SurfaceLutDriver(_scene, color, _colorPack.Luts);
        _lutDriver.CountChanged += attached => BasinReport.Line($"COLOR luts={attached}");
        color.OutputDescriptionChanged += (global, description) =>
            _seat.DescribeCursor(global.Output, description);

        Shells.NewToplevel += window => window.Xdg.Mapped += RefreshLuts;
        _outputs.Added += driver => DescribeOutput(ViewOf(driver));
        _outputs.Removed += view => _outputColor.Remove(view.Global);
        _outputs.LayoutChanged += RefreshLuts;
        foreach (var view in Views)
        {
            DescribeOutput(view);
        }
    }

    private void DescribeOutput(ShellView view) => _outputColor?.Add(view.Global, view.Output, view.SceneOutput);

    private void RefreshLuts() => _lutDriver?.Refresh();
}
