namespace Basin.Capabilities;

/// <summary>Reports a configuration rule whose setting a device did not accept.</summary>
/// <param name="Rule">The index of the rule in the rule list. <c>InputConfig.Describe</c> names its TOML table for a log line.</param>
/// <param name="Device">The identifier of the device, or 0 when the rule failed for every device.</param>
/// <param name="DeviceName">The name of the device, or <see langword="null"/> when no single device applies.</param>
/// <param name="Setting">The setting that failed.</param>
/// <param name="Result">The reason for the failure.</param>
public readonly record struct InputRuleOutcome(
    int Rule,
    ulong Device,
    string? DeviceName,
    InputSetting Setting,
    InputSettingResult Result);
