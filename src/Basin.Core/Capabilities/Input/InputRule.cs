namespace Basin.Capabilities;

/// <summary>A configuration rule that applies settings to the devices that its match selects.</summary>
public sealed class InputRule
{
    /// <summary>Gets the conditions that select the devices.</summary>
    public InputMatch Match { get; init; }

    /// <summary>Gets the settings and the values that the rule applies, in source order.</summary>
    public IReadOnlyList<(InputSetting Setting, InputSettingValue Value)> Settings { get; init; } = [];

    /// <summary>Finds the value that the rule gives a setting. The first entry for the setting wins.</summary>
    /// <param name="setting">The setting to find.</param>
    /// <param name="value">The value, or the default value when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the rule sets <paramref name="setting"/>.</returns>
    public bool Names(InputSetting setting, out InputSettingValue value)
    {
        for (var i = 0; i < Settings.Count; i++)
        {
            if (Settings[i].Setting == setting)
            {
                value = Settings[i].Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
