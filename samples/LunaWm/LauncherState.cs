using System.Text.Json;

namespace LunaWm;

internal sealed class LauncherState
{
    private readonly List<(string Name, List<string> Apps)> _tabs = [];
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly List<string> _quickLaunch = [];

    public IReadOnlyList<(string Name, List<string> Apps)> Tabs => _tabs;

    public IReadOnlySet<string> Hidden => _hidden;

    public IReadOnlyList<string> QuickLaunch => _quickLaunch;

    public static string DefaultPath()
    {
        return Path.Combine(Basin.Freedesktop.XdgDirectories.StateHome, "luna-wm", "launcher.json");
    }

    public static LauncherState Build(IReadOnlyList<LauncherTab> seed, IReadOnlyList<string> seedQuickLaunch, IReadOnlyList<LauncherEntry> entries, LauncherFile? saved)
    {
        var state = new LauncherState();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            known.Add(entry.Id);
        }

        foreach (var tab in seed)
        {
            state._tabs.Add((tab.Name, []));
        }

        var placed = new HashSet<string>(StringComparer.Ordinal);
        if (saved is not null)
        {
            foreach (var hidden in saved.Hidden)
            {
                state._hidden.Add(hidden);
            }

            foreach (var savedTab in saved.Tabs)
            {
                var index = state._tabs.FindIndex(tab => tab.Name == savedTab.Name);
                if (index < 0)
                {
                    state._tabs.Add((savedTab.Name, []));
                    index = state._tabs.Count - 1;
                }

                foreach (var id in savedTab.Apps)
                {
                    if (known.Contains(id) && placed.Add(id))
                    {
                        state._tabs[index].Apps.Add(id);
                    }
                }
            }
        }

        if (state._tabs.Count == 0)
        {
            state._tabs.Add(("Apps", []));
        }

        var unplaced = new List<LauncherEntry>();
        foreach (var entry in entries)
        {
            if (placed.Contains(entry.Id))
            {
                continue;
            }

            var target = SeedTab(seed, entry);
            if (target < 0)
            {
                unplaced.Add(entry);
                continue;
            }

            var index = state._tabs.FindIndex(tab => tab.Name == seed[target].Name);
            state._tabs[index].Apps.Add(entry.Id);
            placed.Add(entry.Id);
        }

        unplaced.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        foreach (var entry in unplaced)
        {
            state._tabs[0].Apps.Add(entry.Id);
        }

        foreach (var id in saved?.QuickLaunch ?? seedQuickLaunch)
        {
            if (known.Contains(id) && state._quickLaunch.Count < 6 && !state._quickLaunch.Contains(id))
            {
                state._quickLaunch.Add(id);
            }
        }

        return state;
    }

    private static int SeedTab(IReadOnlyList<LauncherTab> seed, LauncherEntry entry)
    {
        for (var i = 0; i < seed.Count; i++)
        {
            if (seed[i].Apps.Contains(entry.Id, StringComparer.Ordinal))
            {
                return i;
            }
        }

        for (var i = 0; i < seed.Count; i++)
        {
            foreach (var category in seed[i].Categories)
            {
                if (entry.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    public bool Move(string id, string tab, int position)
    {
        var target = _tabs.FindIndex(t => t.Name == tab);
        if (target < 0)
        {
            return false;
        }

        foreach (var (_, apps) in _tabs)
        {
            apps.Remove(id);
        }

        var list = _tabs[target].Apps;
        list.Insert(Math.Clamp(position, 0, list.Count), id);
        _hidden.Remove(id);
        return true;
    }

    public void Hide(string id) => _hidden.Add(id);

    public void SetQuickLaunch(int slot, string id)
    {
        _quickLaunch.Remove(id);
        if (slot >= _quickLaunch.Count)
        {
            if (_quickLaunch.Count < 6)
            {
                _quickLaunch.Add(id);
            }

            return;
        }

        _quickLaunch[Math.Max(0, slot)] = id;
    }

    public LauncherFile ToFile()
    {
        var file = new LauncherFile { Hidden = [.. _hidden.Order(StringComparer.Ordinal)], QuickLaunch = [.. _quickLaunch] };
        foreach (var (name, apps) in _tabs)
        {
            file.Tabs.Add(new LauncherFileTab { Name = name, Apps = [.. apps] });
        }

        return file;
    }

    public static LauncherFile? Read(string path, out Exception? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, LauncherJsonContext.Default.LauncherFile);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            error = exception;
            return null;
        }
    }

    public bool Write(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out Exception? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, ToFile(), LauncherJsonContext.Default.LauncherFile);
            }

            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception;
            return false;
        }
    }
}
