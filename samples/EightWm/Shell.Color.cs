using Basin;
using Basin.Capabilities;
using Basin.Color;
using Basin.Desktop;

using Basin.Diagnostics;

namespace EightWm;

internal sealed partial class Shell
{
    private ColorManager? _color;
    private ColorLutCache? _luts;
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
        DeclareColor();
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

    private ImageDescription DescriptionOf(IOutput output) => _colorPack.Configuration.DescriptionOf(output);

    private ImageDescription PrimaryDescription() =>
        Views.Count > 0 ? DescriptionOf(Views[0].Output) : ImageDescription.Srgb;

    private void DeclareColor()
    {
        if (_color is { } color)
        {
            SurfaceLutDriver.Declare(color, Views.Select(v => DescriptionOf(v.Output)));
        }
    }

    private void DescribeOutput(ShellView view)
    {
        _color?.SetOutputDescription(view.Global, DescriptionOf(view.Output));
        DeclareColor();
        RefreshLuts();
    }

    private void RefreshLuts()
    {
        if (_color is not { } color || _luts is not { } luts)
        {
            return;
        }

        _resolveLut ??= surface => luts.LutFor(color.DescriptionOf(surface), PrimaryDescription());
        var attached = _scene.AttachLuts(_resolveLut);
        if (attached == _lutCount)
        {
            return;
        }

        _lutCount = attached;
        BasinReport.Line($"COLOR luts={attached}");
    }
}
