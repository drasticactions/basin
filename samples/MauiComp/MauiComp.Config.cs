using Basin;
using Basin.Diagnostics;

namespace MauiComp;

internal sealed partial class MauiComp
{
    private MauiCompConfig _config = new();

    private void Reload()
    {
        var loaded = MauiCompConfig.Load(_options.ConfigPath, _log, out var fatal);
        if (fatal is not null)
        {
            _log.Warn($"reload failed, keeping the running config: {fatal}");
            BasinReport.Line("RELOAD failed");
            return;
        }

        foreach (var key in _config.FromFlags)
        {
            loaded.FromFlags.Add(key);
        }

        if (loaded.FromFlags.Contains("theme"))
        {
            loaded.Theme = _config.Theme;
        }

        var restart = new List<string>();
        if (loaded.FromFlags.Contains("background"))
        {
            loaded.Background = _config.Background;
        }
        else if (loaded.Background != _config.Background)
        {
            restart.Add("background");
            loaded.Background = _config.Background;
        }

        _config = loaded;
        ApplyConfig();
        _outputs.ScheduleAll();
        BasinReport.Line(
            $"RELOAD theme={loaded.Theme}"
            + (restart.Count == 0 ? string.Empty : $" restart-required={string.Join(',', restart)}"));
    }

    private Basin.Effects.IBackdropBlur? _blur;

    private void ApplyConfig()
    {
        _ui.Theme = _config.Theme == "dark"
            ? Basin.UI.Avalonia.UIThemeVariant.Dark
            : Basin.UI.Avalonia.UIThemeVariant.Light;
        if (_blur is not null)
        {
            _blur.Options = _blur.Options with { Strength = _config.BlurStrength };
        }

        PlaceShell();
    }

    private IBackdropEffect? BlurWhen(bool wanted) => wanted ? _blur : null;
}
