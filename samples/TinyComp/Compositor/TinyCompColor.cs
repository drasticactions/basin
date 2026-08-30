using System.Diagnostics;
using Basin;
using Basin.Backend.Libinput;
using Basin.Cli;
using Basin.Effects;
using Basin.Backend.Wayland;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Capabilities;
using Basin.UI.Skia;
using Wayland;
using Wayland.Server;

using Basin.Diagnostics;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private readonly Basin.Color.ColorLutCache _luts;
    private Basin.Desktop.SurfaceLutDriver _lutDriver = null!;

    private Basin.Capabilities.ImageDescription BlendDescription() =>
        _views.Count == 0 || _views[0].KmsColorRouted
            ? Basin.Capabilities.ImageDescription.Srgb
            : _views[0].ColorDescription;

    private void RefreshSurfaceLuts()
    {
        _lutDriver.Refresh();
        UpdateEdrDemand();
    }

    private void UpdateEdrDemand()
    {
        if (_colorConfiguration is not { } colorConfiguration)
        {
            return;
        }

        var demand = 1.0;
        foreach (var surface in _compositor.Surfaces)
        {
            if (_color.DescriptionOf(surface) is not { } description)
            {
                continue;
            }

            var characteristics = Basin.Color.TransferCharacteristics.From(description);
            if (characteristics.MaxLuminance > characteristics.ReferenceLuminance)
            {
                demand = Math.Max(demand, characteristics.MaxLuminance / characteristics.ReferenceLuminance);
            }
        }

        foreach (var view in _views)
        {
            colorConfiguration.SetEdrDemand(view.Output, demand);
        }
    }

    private double? _nightLightKelvin;

    private void ApplyNightLight(double? kelvin)
    {
        _nightLightKelvin = kelvin;
        foreach (var view in _views)
        {
            if (_gamma.RampSize(view.Output) > 0)
            {
                RefreshGammaBaseline(view);
                _gamma.ApplyBaseline(view.Output);
            }
        }
    }

    private void RefreshGammaBaseline(OutputView view)
    {
        if (_gamma.RampSize(view.Output) is var size && size > 0)
        {
            _gamma.Baseline = _nightLightKelvin is { } k
                ? NightLightRamps(view, (int)size, k)
                : view.KmsColorRouted && _colorConfiguration is { } configuration
                    ? configuration.RoutedEncodeRamps(view.Output)
                    : null;
        }
    }

    private OutputGammaRamps NightLightRamps(OutputView view, int size, double kelvin)
    {
        if (view.KmsColorRouted && _colorConfiguration is { } configuration &&
            configuration.RoutedEncodeRamps(view.Output, Basin.Color.NightLight.Multipliers(kelvin)) is { } routed)
        {
            return routed;
        }

        var ramps = new OutputGammaRamps(new ushort[size], new ushort[size], new ushort[size]);
        Basin.Color.NightLight.FillGammaRamps(kelvin, ramps.Red, ramps.Green, ramps.Blue);
        return ramps;
    }
}
