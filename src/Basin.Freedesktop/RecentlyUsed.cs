using System.Xml.Linq;

namespace Basin.Freedesktop;

/// <summary>
/// Reads the <c>recently-used.xbel</c> bookmark file that GTK and KDE write.
/// </summary>
public static class RecentlyUsed
{
    private static readonly XNamespace Mime = "http://www.freedesktop.org/standards/shared-mime-info";

    /// <summary>
    /// Gets the path of <c>recently-used.xbel</c> under <see cref="XdgDirectories.DataHome"/>. Each read evaluates the environment again.
    /// </summary>
    public static string DefaultPath => Path.Combine(XdgDirectories.DataHome, "recently-used.xbel");

    /// <summary>
    /// Reads the <c>file://</c> bookmarks of the file. Bookmarks with other schemes are skipped.
    /// </summary>
    /// <param name="path">The file to read, or <see langword="null"/> for <see cref="DefaultPath"/>.</param>
    /// <returns>The items, newest first by their <c>modified</c> stamp. A missing file gives an empty list.</returns>
    /// <exception cref="System.Xml.XmlException">The file exists and is not well-formed XML.</exception>
    public static IReadOnlyList<RecentlyUsedItem> Read(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
        {
            return [];
        }

        var items = new List<RecentlyUsedItem>();
        foreach (var bookmark in XDocument.Load(path).Root?.Elements("bookmark") ?? [])
        {
            var href = bookmark.Attribute("href")?.Value;
            if (href is null || !href.StartsWith("file://", StringComparison.Ordinal))
            {
                continue;
            }

            var mimeType = bookmark.Descendants(Mime + "mime-type").FirstOrDefault()?.Attribute("type")?.Value;
            var modified = DateTime.TryParse(bookmark.Attribute("modified")?.Value, out var stamp) ? stamp : DateTime.MinValue;
            items.Add(new RecentlyUsedItem(Uri.UnescapeDataString(new Uri(href).LocalPath), mimeType, modified));
        }

        items.Sort(static (a, b) => b.Modified.CompareTo(a.Modified));
        return items;
    }
}
