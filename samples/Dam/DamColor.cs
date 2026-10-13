using Basin;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Host;

namespace Dam;

internal sealed partial class Dam
{
    private void WireColor()
    {
        if (!_colorPack.Attach(_services, _scene))
        {
            return;
        }

        _colorPack.Surfaces.CountChanged += attached => BasinReport.Line(CompositorLines.Luts(attached));
        _outputs.Added += DescribeOutput;
        _outputs.Removed += view => _colorPack.RemoveOutput(view.Global);
        foreach (var view in _outputs.Views)
        {
            DescribeOutput(view);
        }
    }

    private void DescribeOutput(OutputView view)
    {
        _colorPack.AddOutput(view.Global, view.Output, view.Scene);
        if (view.Output is Basin.Backend.Drm.DrmOutput card && DrivesHdr(card) && view.Scene is { } scene)
        {
            scene.OutputHdrMetadata = Basin.Color.OutputDescriptions.HdrMetadataFor(
                _colorPack.Configuration.DescriptionOf(card), _colorPack.Configuration.ColorimetryOf(card)?.Chromaticities ?? card.Edid.Chromaticities);
            scene.HdrMetadataPassthrough = true;
        }
    }

    private bool DrivesHdr(Basin.Backend.Drm.DrmOutput card) =>
        _options.InverseToneMapping && (_panels.Match(card)?.Hdr ?? card.Edid.SupportsPq) &&
        (_colorPack.Configuration.Supported(card) & Basin.Capabilities.OutputConfigurationFeatures.HighDynamicRange) != 0;

    private void OnStampModeset(Basin.Backend.Drm.DrmOutput card, OutputState state)
    {
        var panel = _panels.Match(card);
        _colorPack.Configuration.SetPanel(card, panel);
        card.ModeGenerator = panel?.ModeGenerator ?? Basin.Capabilities.OutputModeGenerator.Auto;
        card.MaxClockKhz = panel?.MaxClockKhz ?? 0;
        if (panel?.Orientation is { } orientation)
        {
            state.SetTransform(orientation);
        }

        if (panel is { RefreshMilliHz.Count: > 0 } && card.PreferredMode.Width > 0)
        {
            var preferred = card.PreferredMode;
            state.SetCustomModes([.. panel.RefreshMilliHz.Select(rate => new OutputMode(preferred.Width, preferred.Height, rate))]);
        }

        if (!DrivesHdr(card))
        {
            return;
        }

        _colorPack.Configuration.Seed(card, new Basin.Color.OutputColorState { HighDynamicRange = true });
        _colorPack.Configuration.SetInverseToneMapping(card, new Basin.Capabilities.InverseToneMapping());
        state.SetHdr(Basin.Color.OutputDescriptions.HdrMetadataFor(
            _colorPack.Configuration.DescriptionOf(card), _colorPack.Configuration.ColorimetryOf(card)?.Chromaticities ?? card.Edid.Chromaticities));
    }
}
