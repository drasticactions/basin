using Basin.Cli;
using Basin.Host;

namespace Westonia;

internal sealed partial class Westonia
{
    private void WireColor()
    {
        if (!_colorPack.Attach(_services, _scene))
        {
            _log.Info($"color: no color manager, color management is off");
            return;
        }

        _colorPack.Surfaces.CountChanged += attached => _report.Line(CompositorLines.Luts(attached));
        _colorPack.Manager.OutputDescriptionChanged +=
            (global, description) => _cursor.Describe(global.Output, description);
    }

    private void DescribeOutput(OutputView view) => _colorPack.AddOutput(view.Global, view.Output, view.Scene);

    private void ForgetOutput(OutputView view) => _colorPack.RemoveOutput(view.Global);

    internal void RefreshSurfaceLuts() => _colorPack.RefreshSurfaces();
}
