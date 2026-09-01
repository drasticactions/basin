using System.Xml.Linq;
using Basin.Config;

using Basin.Diagnostics;

namespace DeskbarWm;

internal sealed class RecentItems
{
    private readonly List<string> _recentApps = [];
    private readonly BasinLogger _log;

    public RecentItems(BasinLogger log)
    {
        _log = log;
        try
        {
            if (File.Exists(RecentAppsPath))
            {
                foreach (var line in File.ReadLines(RecentAppsPath))
                {
                    if (line.Length > 0)
                    {
                        _recentApps.Add(line);
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public IReadOnlyList<string> RecentApplications => _recentApps;

    public void RecordLaunch(string appId, int keep)
    {
        _recentApps.Remove(appId);
        _recentApps.Insert(0, appId);
        while (_recentApps.Count > Math.Max(keep, 1))
        {
            _recentApps.RemoveAt(_recentApps.Count - 1);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecentAppsPath)!);
            File.WriteAllLines(RecentAppsPath, _recentApps);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"could not save the recent applications: {error.Message}");
        }
    }

    public (IReadOnlyList<(string Path, string Label)> Documents, IReadOnlyList<(string Path, string Label)> Folders)
        ReadBookmarks(int documentCount, int folderCount)
    {
        var documents = new List<(string, string)>();
        var folders = new List<(string, string)>();
        try
        {
            var xbelPath = Path.Combine(
                Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
                "recently-used.xbel");
            if (!File.Exists(xbelPath))
            {
                return (documents, folders);
            }

            XNamespace mime = "http://www.freedesktop.org/standards/shared-mime-info";
            var document = XDocument.Load(xbelPath);
            var bookmarks = new List<(string Href, string? Mime, DateTime Modified)>();
            foreach (var bookmark in document.Root?.Elements("bookmark") ?? [])
            {
                var href = bookmark.Attribute("href")?.Value;
                if (href is null || !href.StartsWith("file://", StringComparison.Ordinal))
                {
                    continue;
                }

                var mimeType = bookmark.Descendants(mime + "mime-type").FirstOrDefault()?.Attribute("type")?.Value;
                var modified = DateTime.TryParse(
                    bookmark.Attribute("modified")?.Value, out var stamp)
                    ? stamp
                    : DateTime.MinValue;
                bookmarks.Add((href, mimeType, modified));
            }

            bookmarks.Sort(static (a, b) => b.Modified.CompareTo(a.Modified));
            foreach (var (href, mimeType, _) in bookmarks)
            {
                var path = Uri.UnescapeDataString(new Uri(href).LocalPath);
                var label = Path.GetFileName(path);
                if (label.Length == 0)
                {
                    continue;
                }

                if (mimeType == "inode/directory")
                {
                    if (folders.Count < folderCount)
                    {
                        folders.Add((path, label));
                    }
                }
                else if (documents.Count < documentCount)
                {
                    documents.Add((path, label));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            _log.Warn($"could not read recently-used.xbel: {error.Message}");
        }

        return (documents, folders);
    }

    private static string RecentAppsPath =>
        Path.Combine(Path.GetDirectoryName(TomlConfig.DefaultPath("deskbar-wm"))!, "recent-apps");
}
