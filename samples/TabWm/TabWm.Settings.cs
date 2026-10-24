using System.Globalization;
using System.Reflection;
using AvaUnion.Engine;
using Avalonia;
using Basin;
using Basin.Capabilities;
using Basin.Config;
using Basin.Diagnostics;
using Basin.Freedesktop;
using TabWm.Chrome;

namespace TabWm;

internal sealed partial class TabWm
{
    internal const int SettingsGeneral = 0;
    internal const int SettingsKeys = 1;
    internal const int SettingsAbout = 2;

    internal SettingsViewModel? Settings => _overlay?.View.DataContext as SettingsViewModel;

    internal void ShowSettings(TabSet set, int section)
    {
        var model = CreateSettingsModel(section);
        var view = new SettingsView { DataContext = model };
        if (OpenOverlay(set, view, null, "Settings", 760, 620))
        {
            BasinReport.Line($"SETTINGS shown section={SectionName(section)} output={set.Output.Name}");
        }
    }

    private static string SectionName(int section) => section switch
    {
        SettingsKeys => "keys",
        SettingsAbout => "about",
        _ => "general",
    };

    private SettingsViewModel CreateSettingsModel(int section)
    {
        var path = _config.Path;
        var file = _config;
        var refusal = path is null ? "tab-wm is running with --config false, so there is no file to save to." : null;
        if (path is not null && File.Exists(path))
        {
            try
            {
                file = TabWmConfig.FromText(File.ReadAllText(path), _log);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or Tomlyn.TomlException)
            {
                refusal = $"The settings file cannot be read: {error.Message}";
            }
        }

        var schemes = ColorSchemeNames();
        var model = new SettingsViewModel
        {
            Section = section,
            ColorSchemes = ["Session default", .. schemes],
            Keys = [.. _config.Bindings.Select(b => new SettingsKeyModel(b.Chord, b.Label))],
            ConfigPath = path ?? "(none)",
            CanSave = refusal is null,
            Version = $"Version {VersionOf(typeof(TabWm).Assembly)}",
            BasinVersion = VersionOf(typeof(Basin.Box).Assembly),
            Backend = _options.Backend.ToString().ToLowerInvariant(),
            Renderer = _rendererName,
            Engine = (EngineSelector.Selected ?? AvaUnion.Elements.UnionEnvironment.Engine).Kind == UnionEngineKind.Native ? "native (Qt)" : "managed",
            Status = refusal ?? string.Empty,
        };
        model.Load(() =>
        {
            model.Variant = (int)file.Variant;
            model.ColorScheme = file.ColorScheme is { } scheme && IndexOf(schemes, scheme) is >= 0 and var index ? index + 1 : 0;
            model.EngineChoice = file.Engine switch
            {
                UnionEngineKind.Native => 1,
                UnionEngineKind.Managed => 2,
                _ => 0,
            };
            model.CloseWhenEmpty = file.CloseWhenEmpty;
            model.CloseTimeoutSeconds = file.CloseTimeoutMs / 1000m;
            model.Restore = file.Restore;
            model.Terminal = file.Terminal ?? string.Empty;
            model.LockCommand = file.LockCommand ?? string.Empty;
            model.XWayland = file.XWayland;
            model.HighContrast = file.Accessibility.HighContrast;
            model.ReducedMotion = file.Accessibility.ReducedMotion;
        });
        model.Changed = (name, value) => OnSettingChanged(model, name, value, schemes);
        model.Dismiss = CloseOverlay;
        return model;
    }

    private void OnSettingChanged(SettingsViewModel model, string name, object value, IReadOnlyList<string> schemes)
    {
        switch (name)
        {
            case nameof(SettingsViewModel.Variant):
                _config.Variant = (ThemeVariantChoice)(int)value;
                Save(model, "theme", "variant", TomlValue.From(_config.Variant.ToString().ToLowerInvariant()));
                ApplyTheme();
                ApplyAppearance();
                break;
            case nameof(SettingsViewModel.ColorScheme):
                var scheme = (int)value is > 0 and var index && index <= schemes.Count ? schemes[index - 1] : null;
                _config.ColorScheme = scheme;
                Save(model, "theme", "color-scheme", TomlValue.From(scheme ?? string.Empty));
                ApplyTheme();
                break;
            case nameof(SettingsViewModel.EngineChoice):
                Save(model, "theme", "engine", TomlValue.From((int)value switch { 1 => "native", 2 => "managed", _ => "auto" }));
                break;
            case nameof(SettingsViewModel.CloseWhenEmpty):
                _config.CloseWhenEmpty = (bool)value;
                Save(model, "tabs", "close-when-empty", TomlValue.From(_config.CloseWhenEmpty));
                break;
            case nameof(SettingsViewModel.CloseTimeoutSeconds):
                _config.CloseTimeoutMs = (int)Math.Round((decimal)value * 1000m);
                Save(model, "tabs", "close-timeout-ms", TomlValue.From(_config.CloseTimeoutMs));
                break;
            case nameof(SettingsViewModel.Restore):
                Save(model, "session", "restore", TomlValue.From((bool)value));
                break;
            case nameof(SettingsViewModel.Terminal):
                var terminal = ((string)value).Trim();
                _config.Terminal = terminal.Length > 0 ? terminal : null;
                Save(model, "launcher", "terminal", TomlValue.From(terminal));
                break;
            case nameof(SettingsViewModel.LockCommand):
                var lockCommand = ((string)value).Trim();
                _config.LockCommand = lockCommand;
                Save(model, "session", "lock-command", TomlValue.From(lockCommand));
                break;
            case nameof(SettingsViewModel.HighContrast):
                _config.Accessibility = _config.Accessibility with { HighContrast = (bool)value };
                Save(model, "accessibility", "high-contrast", TomlValue.From((bool)value));
                ApplyTheme();
                ApplyAppearance();
                break;
            case nameof(SettingsViewModel.ReducedMotion):
                _config.Accessibility = _config.Accessibility with { ReducedMotion = (bool)value };
                Save(model, "accessibility", "reduced-motion", TomlValue.From((bool)value));
                ApplyAppearance();
                break;
            case nameof(SettingsViewModel.XWayland):
                Save(model, "shell", "xwayland", TomlValue.From((bool)value));
                break;
        }
    }

    private void Save(SettingsViewModel model, string table, string key, TomlValue value)
    {
        if (_config.Path is not { } path)
        {
            return;
        }

        try
        {
            var document = TomlDocument.LoadOrEmpty(path);
            document.Set(table, key, value);
            document.Save(path);
            model.Status = string.Empty;
            BasinReport.Line($"SETTINGS saved {table}.{key}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            _log.Warn(error, $"settings: cannot write {table}.{key} to {path}");
            model.Status = $"The setting was not saved: {error.Message}";
        }
    }

    private void ApplyTheme()
    {
        if (Application.Current is not TabWmApp app)
        {
            return;
        }

        app.Union.FollowThemeVariant = true;
        app.Union.ColorScheme = EffectiveColorScheme(_config);
        _outputs.ScheduleAll();
    }

    private void ApplyAppearance()
    {
        var accessibility = _config.Accessibility;
        ReducedMotion.IsOn = accessibility.ReducedMotion;
        _access?.Apply(accessibility);

        _appearance.Set(new AppearancePreferences(
            _config.Variant switch
            {
                ThemeVariantChoice.Dark => ColorSchemePreference.Dark,
                ThemeVariantChoice.Light => ColorSchemePreference.Light,
                _ => ColorSchemePreference.None,
            },
            null,
            accessibility.HighContrast,
            accessibility.ReducedMotion));
    }

    internal static string HighContrastScheme()
    {
        var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } set
            ? set
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        var path = Path.Combine(cache, "tab-wm", "HighContrast.colors");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = typeof(TabWm).Assembly.GetManifestResourceStream("HighContrast.colors")
                ?? throw new InvalidOperationException("The high-contrast color scheme is not embedded in the program.");
            using var target = File.Create(path);
            source.CopyTo(target);
        }

        return path;
    }

    internal static string? EffectiveColorScheme(TabWmConfig config) => config.Accessibility.HighContrast ? HighContrastScheme() : config.ColorScheme ?? config.Variant switch
    {
        ThemeVariantChoice.Light => "BreezeLight",
        ThemeVariantChoice.Dark => "BreezeDark",
        _ => null,
    };

    private static List<string> ColorSchemeNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in XdgDirectories.DataDirectories())
        {
            var schemes = Path.Combine(directory, "color-schemes");
            if (!Directory.Exists(schemes))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(schemes, "*.colors"))
            {
                names.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        return [.. names];
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        var bare = Path.GetFileNameWithoutExtension(name);
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], bare, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string VersionOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is null)
        {
            return assembly.GetName().Version?.ToString() ?? "unknown";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
