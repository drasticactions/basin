using System.Diagnostics;
using Basin;
using Basin.Host;
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
    private void SyncBlendSpace(OutputView view) =>
        _colorPack.Outputs?.SetBlendSpace(
            view.Global, view.KmsColorRouted ? Basin.Capabilities.ImageDescription.SdrDefault : null);

    private void RefreshSurfaceLuts()
    {
        _colorPack.RefreshSurfaces();
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

        foreach (var view in Views)
        {
            colorConfiguration.SetEdrDemand(view.Output, demand);
        }
    }

    private bool _hdrShown;

    private Basin.Desktop.HdrDemand? _hdrDemand;

    private void OnHdrDemandChanged(bool shown)
    {
        _hdrShown = shown;
        if (_colorConfiguration is not { } configuration)
        {
            return;
        }

        foreach (var view in Views)
        {
            configuration.SetHdrDemand(view.Output, shown);
        }
    }

    private void ApplyColorLooks()
    {
        foreach (var view in Views)
        {
            if (view.Output is Basin.Backend.Drm.DrmOutput drmOutput)
            {
                ApplyColorLooks(drmOutput, _hdr && (_panels.Match(drmOutput)?.Hdr ?? drmOutput.Edid.SupportsPq));
            }
        }
    }

    private void ApplyColorLooks(IOutput output, bool hdr)
    {
        if (_colorConfiguration is not { } configuration)
        {
            return;
        }

        configuration.SetHdrOnDemand(output, hdr && _config.HdrDemand);
        configuration.SetInverseToneMapping(
            output, hdr && _config.InverseToneMapping ? new Basin.Capabilities.InverseToneMapping(_config.ItmSdrNits, _config.ItmTargetNits) : null);
        if (configuration.LookOf(output) != _config.Look && !configuration.SetLook(output, _config.Look, out var error))
        {
            _report.Line($"LOOK {output.Name} failed {error}");
        }
    }

    private double? _nightLightKelvin;

    private void ApplyNightLight(double? kelvin)
    {
        _nightLightKelvin = kelvin;
        foreach (var view in Views)
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
