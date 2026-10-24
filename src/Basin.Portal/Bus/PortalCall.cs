namespace Basin.Portal;

/// <summary>
/// The origin of the D-Bus method call that a module handles now.
/// </summary>
/// <param name="Sender">The unique bus name of the caller.</param>
/// <param name="Path">The object path that the call addressed.</param>
public readonly record struct PortalCall(string Sender, string Path);
