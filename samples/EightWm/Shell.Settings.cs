using Avalonia.Media;
using AvaWin;
using Basin.UI.Avalonia;

namespace EightWm;

internal sealed partial class Shell
{
    internal const uint DefaultAccent = 0xff2d89ef;

    private bool? _liveDark;
    private uint? _liveAccent;
    private bool? _liveAnimations;
    private bool? _liveHotCorners;
    private readonly List<SettingsModel> _settingsModels = [];
    private bool _syncingSettings;

    internal bool DarkNow => _liveDark ?? (Setting("theme") ? _options.Dark : _config.Dark);

    internal uint AccentNow => _liveAccent ?? (Setting("accent") ? _options.Accent : _config.Accent);

    internal IReadOnlyList<uint> Accents => AccentPalette;

    private void AttachSettings(SettingsModel model)
    {
        model.SetPalette(AccentPalette);
        model.Load(DarkNow, AccentNow, AnimationsOn, HotCornersOn);
        model.Changed += name => OnSettingChanged(model, name);
        _settingsModels.Add(model);
    }

    private void OnSettingChanged(SettingsModel model, string name)
    {
        if (_syncingSettings)
        {
            return;
        }

        switch (name)
        {
            case nameof(SettingsModel.Dark):
                _liveDark = model.Dark;
                break;
            case nameof(SettingsModel.Accent):
                _liveAccent = model.Accent;
                break;
            case nameof(SettingsModel.Animations):
                _liveAnimations = model.Animations;
                break;
            case nameof(SettingsModel.HotCorners):
                _liveHotCorners = model.HotCorners;
                break;
        }

        ApplySettings();
        Basin.Diagnostics.BasinReport.Line($"SETTING {name.ToLowerInvariant()}={SettingValue(name)}");
    }

    private string SettingValue(string name) => name switch
    {
        nameof(SettingsModel.Dark) => DarkNow ? "dark" : "light",
        nameof(SettingsModel.Accent) => $"#{AccentNow & 0xffffff:x6}",
        nameof(SettingsModel.Animations) => AnimationsOn ? "on" : "off",
        _ => HotCornersOn ? "on" : "off",
    };

    private void ClearLiveSettings()
    {
        _liveDark = null;
        _liveAccent = null;
        _liveAnimations = null;
        _liveHotCorners = null;
    }

    private string? _appliedFont;

    private void ApplyFont(string? spec)
    {
        if (spec == _appliedFont || Avalonia.Application.Current is not { } app)
        {
            return;
        }

        _appliedFont = spec;
        if (FontFamilyOf(spec) is not { } family)
        {
            app.Resources.Remove("WinFontFamily");
            app.Resources.Remove("ContentControlThemeFontFamily");
            return;
        }

        app.Resources["WinFontFamily"] = family;
        app.Resources["ContentControlThemeFontFamily"] = family;
    }

    private FontFamily? FontFamilyOf(string? spec)
    {
        if (string.IsNullOrEmpty(spec))
        {
            return null;
        }

        if (!spec.Contains('/'))
        {
            return new FontFamily(spec);
        }

        if (!File.Exists(spec))
        {
            _log.Warn($"[ui] font {spec} does not exist; the theme font is used");
            return null;
        }

        using var face = SkiaSharp.SKTypeface.FromFile(spec);
        if (face?.FamilyName is not { Length: > 0 } name)
        {
            _log.Warn($"[ui] font {spec} is not a font file; the theme font is used");
            return null;
        }

        using var installed = SkiaSharp.SKFontManager.Default.MatchFamily(name);
        return installed is not null && installed.FamilyName == name
            ? new FontFamily(name)
            : new FontFamily($"{new Uri(Path.GetFullPath(spec)).AbsoluteUri}#{name}");
    }

    internal void ApplySettings()
    {
        _ui.Theme = DarkNow ? UIThemeVariant.Dark : UIThemeVariant.Light;
        if (Avalonia.Application.Current is { } app)
        {
            foreach (var style in app.Styles)
            {
                if (style is AvaWinTheme theme)
                {
                    theme.AccentColor = Color.FromUInt32(AccentNow);
                }
            }
        }

        ApplyFont(_config.Font);
        AvaWin.Animations.WinAnimations.IsEnabled = AnimationsOn;
        _syncingSettings = true;
        try
        {
            foreach (var model in _settingsModels)
            {
                model.SetPalette(AccentPalette);
                model.Load(DarkNow, AccentNow, AnimationsOn, HotCornersOn);
            }
        }
        finally
        {
            _syncingSettings = false;
        }

        _outputs.ScheduleAll();
    }
}
