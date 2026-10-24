using Basin.Capabilities;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;

namespace Basin.Portal;

/// <summary>
/// Serves <c>org.freedesktop.impl.portal.Settings</c> for the <c>org.freedesktop.appearance</c> namespace:
/// <c>color-scheme</c>, <c>accent-color</c>, <c>contrast</c> and <c>reduced-motion</c>, read from <see cref="IAppearanceSettings"/>.
/// It installs only when the compositor registers an <see cref="IAppearanceSettings"/>, so another backend can answer for a session without one.
/// </summary>
public sealed class PortalSettingsModule : PortalModule, ISettingsHandler, ISettingsProperties
{
    /// <summary>
    /// The namespace that the module serves.
    /// </summary>
    public const string Appearance = "org.freedesktop.appearance";

    private IAppearanceSettings? _settings;
    private AppearancePreferences _sent;

    /// <inheritdoc/>
    public override string WireInterface => "org.freedesktop.impl.portal.Settings";

    /// <inheritdoc/>
    public override int Version => 2;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IAppearanceSettings)];

    uint ISettingsProperties.Version => (uint)Version;

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalSettings;

    /// <summary>
    /// Reads every appearance setting when a pattern matches the namespace. An empty list matches every namespace, and a pattern can end with <c>*</c>.
    /// </summary>
    /// <param name="namespaces">The namespace patterns.</param>
    /// <returns>The settings of each matching namespace.</returns>
    public ValueTask<Dictionary<string, Dictionary<string, VariantValue>>> ReadAllAsync(string[] namespaces)
    {
        var result = new Dictionary<string, Dictionary<string, VariantValue>>();
        if (Matches(namespaces) && _settings is { } settings)
        {
            result[Appearance] = Values(settings.Current);
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// Reads one setting.
    /// </summary>
    /// <param name="namespace">The namespace.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value.</returns>
    /// <exception cref="DBusErrorReplyException">The namespace or the key is unknown, or no appearance settings are registered.</exception>
    public ValueTask<VariantValue> ReadAsync(string @namespace, string key)
    {
        if (@namespace == Appearance && _settings is { } settings && Values(settings.Current).TryGetValue(key, out var value))
        {
            return ValueTask.FromResult(value);
        }

        throw PortalError.Missing($"{@namespace} {key} is not set");
    }

    ValueTask ISettingsHandler.HandleGetPropertyAsync(ISettingsHandler.GetPropertyContext context) => context.Handle(this);

    ValueTask ISettingsHandler.HandleGetAllPropertiesAsync(ISettingsHandler.GetAllPropertiesContext context) => context.Handle(this);

    /// <summary>
    /// Offers the interface only when a bus and an <see cref="IAppearanceSettings"/> are registered.
    /// </summary>
    /// <param name="services">The registry after all defaults are seeded.</param>
    /// <returns><see langword="true"/> when both are registered.</returns>
    public override bool ShouldInstall(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Find<IAppearanceSettings>() is null)
        {
            PortalLog.Log.Info($"{WireInterface} not offered, the compositor registers no appearance settings");
            return false;
        }

        return base.ShouldInstall(services);
    }

    /// <inheritdoc/>
    protected override void Bind(BasinServices services)
    {
        _settings = services.Find<IAppearanceSettings>();
        if (_settings is { } settings)
        {
            _sent = settings.Current;
            settings.Changed += OnChanged;
        }
    }

    /// <inheritdoc/>
    protected override void Unbind()
    {
        if (_settings is { } settings)
        {
            settings.Changed -= OnChanged;
        }

        _settings = null;
    }

    private static bool Matches(string[] namespaces)
    {
        if (namespaces.Length == 0)
        {
            return true;
        }

        foreach (var pattern in namespaces)
        {
            if (pattern.Length == 0 || pattern == Appearance || (pattern.EndsWith('*') && Appearance.StartsWith(pattern[..^1], StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, VariantValue> Values(in AppearancePreferences preferences)
    {
        var values = new Dictionary<string, VariantValue>
        {
            ["color-scheme"] = VariantValue.UInt32((uint)preferences.ColorScheme),
            ["contrast"] = VariantValue.UInt32(preferences.HighContrast ? 1u : 0u),
            ["reduced-motion"] = VariantValue.UInt32(preferences.ReducedMotion ? 1u : 0u),
        };
        if (preferences.Accent is { } accent)
        {
            values["accent-color"] = VariantValue.Struct(VariantValue.Double(accent.Red), VariantValue.Double(accent.Green), VariantValue.Double(accent.Blue));
        }

        return values;
    }

    private void OnChanged()
    {
        if (_settings is not { } settings || Bus?.Connection is not { } connection)
        {
            return;
        }

        var before = Values(_sent);
        var now = Values(settings.Current);
        _sent = settings.Current;
        foreach (var (key, value) in now)
        {
            if (!before.TryGetValue(key, out var old) || old.ToString() != value.ToString())
            {
                connection.EmitSettingChanged(new ObjectPath(PortalBus.RootPath), Appearance, key, value);
            }
        }

        if (before.ContainsKey("accent-color") && !now.ContainsKey("accent-color"))
        {
            connection.EmitSettingChanged(new ObjectPath(PortalBus.RootPath), Appearance, "accent-color", VariantValue.Struct(VariantValue.Double(-1), VariantValue.Double(-1), VariantValue.Double(-1)));
        }
    }
}
