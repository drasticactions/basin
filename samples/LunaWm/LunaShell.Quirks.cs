using Basin.Config;
using Basin.Scene;
using Basin.XWayland;

namespace LunaWm;

internal sealed partial class LunaShell
{
    private void ApplyQuirks(Card card)
    {
        var quirks = WindowRule.QuirksFor(_config.Rules, card.Facts);
        if (card.X11 is { } x11)
        {
            x11.Letterbox = quirks.Letterbox switch
            {
                LetterboxQuirk.Fit => SceneFitPolicy.Fit,
                LetterboxQuirk.Center => SceneFitPolicy.Center,
                LetterboxQuirk.Off => SceneFitPolicy.Off,
                _ => SceneFitPolicy.Auto,
            };
            x11.Readiness = quirks.Readiness == ReadinessQuirk.Immediate
                ? XWaylandReadiness.Immediate
                : XWaylandReadiness.OnMatchingCommit;
            x11.ForceUnmaximize = quirks.ForceUnmaximize ?? false;
            x11.AllowClientMove = quirks.AllowClientMove ?? false;
            x11.HonorEmulatedModes = quirks.EmulatedModes ?? true;
            _xwayland.Refit(x11);
        }

        card.Content.Filter = quirks.ScaleFilter;
        if (card.Xdg is { } xdg)
        {
            xdg.Xdg.WidenPresentedGeometry = quirks.WidenGeometry;
        }
    }
}
