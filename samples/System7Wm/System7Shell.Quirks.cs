using Basin.Config;
using Basin.Scene;
using Basin.XWayland;

namespace System7Wm;

internal sealed partial class System7Shell
{
    private void ApplyQuirks(ShellWindow window)
    {
        var quirks = WindowRule.QuirksFor(_config.Rules, window.Facts);
        if (window.X11 is { } x11)
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
            _x11?.Refit(x11);
        }

        if (window.ContentNode is SceneTree content)
        {
            content.Filter = quirks.ScaleFilter;
        }
    }
}
