namespace Basin.Freedesktop;

/// <summary>
/// The <c>Type=</c> key of a desktop entry.
/// </summary>
public enum DesktopEntryType
{
    /// <summary>A missing <c>Type</c> or a value that the specification does not define. Such an entry is never listed.</summary>
    Unknown,
    /// <summary>An application that <c>Exec</c> starts.</summary>
    Application,
    /// <summary>A link to the address in <c>URL</c>.</summary>
    Link,
    /// <summary>A menu directory entry.</summary>
    Directory,
}
