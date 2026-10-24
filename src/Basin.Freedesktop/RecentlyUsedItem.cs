namespace Basin.Freedesktop;

/// <summary>
/// One local file from <c>recently-used.xbel</c>.
/// </summary>
/// <param name="Path">The local file path, with percent escapes decoded.</param>
/// <param name="MimeType">The MIME type that the bookmark records, or <see langword="null"/> when it records none.</param>
/// <param name="Modified">The <c>modified</c> stamp of the bookmark, or <see cref="DateTime.MinValue"/> when it does not parse.</param>
public readonly record struct RecentlyUsedItem(string Path, string? MimeType, DateTime Modified)
{
    /// <summary>
    /// Gets a value indicating whether the bookmark records the MIME type <c>inode/directory</c>. It does not check the file system.
    /// </summary>
    public bool IsDirectory => MimeType == "inode/directory";
}
