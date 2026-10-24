namespace Basin.Capabilities.Defaults;

/// <summary>
/// An <see cref="IAppearanceSettings"/> that holds the values a compositor sets, for example from its configuration or its settings page.
/// </summary>
public sealed class AppearanceSettingsStore : IAppearanceSettings
{
    /// <inheritdoc/>
    public AppearancePreferences Current { get; private set; }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>
    /// Sets the preferences. It raises <see cref="Changed"/> only when they differ. Call it on the compositor loop.
    /// </summary>
    /// <param name="preferences">The new preferences.</param>
    public void Set(in AppearancePreferences preferences)
    {
        if (preferences == Current)
        {
            return;
        }

        Current = preferences;
        Changed?.Invoke();
    }
}
