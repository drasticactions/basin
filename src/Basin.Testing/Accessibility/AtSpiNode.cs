namespace Basin.Testing;

/// <summary>
/// One accessible object that an AT-SPI walk found.
/// </summary>
/// <param name="Bus">The unique bus name of the application that owns the object.</param>
/// <param name="Path">The object path of the object.</param>
/// <param name="Role">The AT-SPI role name, for example "push button".</param>
/// <param name="Name">The accessible name.</param>
public readonly record struct AtSpiNode(string Bus, string Path, string Role, string Name);
