namespace Basin.Capabilities;

/// <summary>
/// One scene damage event that <see cref="ISceneDiagnostics.WatchDamage"/> reports.
/// </summary>
/// <param name="Path">The scene path of the node that caused the damage, or "(scene)" when no node did.</param>
/// <param name="Box">The damaged box in scene coordinates.</param>
/// <param name="Stack">The stack trace of the first damage from this path when the watch asked for stacks. Otherwise <see langword="null"/>.</param>
public readonly record struct DamageInfo(string Path, Box Box, string? Stack);
