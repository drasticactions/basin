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
            _log.Warn(error, $"recent: cannot read the recent applications from {RecentAppsPath}");
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
            _log.Warn(error, $"recent: cannot save the recent applications to {RecentAppsPath}");
        }
    }

    public (IReadOnlyList<(string Path, string Label)> Documents, IReadOnlyList<(string Path, string Label)> Folders)
        ReadBookmarks(int documentCount, int folderCount)
    {
        var documents = new List<(string, string)>();
        var folders = new List<(string, string)>();
        try
        {
            foreach (var item in Basin.Freedesktop.RecentlyUsed.Read())
            {
                var label = Path.GetFileName(item.Path);
                if (label.Length == 0)
                {
                    continue;
                }

                if (item.IsDirectory)
                {
                    if (folders.Count < folderCount)
                    {
                        folders.Add((item.Path, label));
                    }
                }
                else if (documents.Count < documentCount)
                {
                    documents.Add((item.Path, label));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            _log.Warn(error, $"recent: cannot read recently-used.xbel");
        }

        return (documents, folders);
    }

    private static string RecentAppsPath =>
        Path.Combine(Path.GetDirectoryName(TomlConfig.DefaultPath("deskbar-wm"))!, "recent-apps");
}
