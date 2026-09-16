using System.Text;
using Basin.Diagnostics;

namespace Basin.Freedesktop;

public static class DesktopEntryReader
{
    private static readonly BasinLogger Log = BasinLog.For("freedesktop");

    public static DesktopEntry? Parse(string path, string id, DesktopLocale locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(id);
        return KeyFile.Read(path) is { } groups ? Build(groups, path, id, locale) : null;
    }

    public static DesktopEntry? ParseText(string text, string path, string id, DesktopLocale locale)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(id);
        return Build(KeyFile.Parse(text.Split('\n'), path), path, id, locale);
    }

    private static DesktopEntry? Build(List<KeyFileGroup> groups, string path, string id, DesktopLocale locale)
    {
        var entry = new Group();
        var actions = new Dictionary<string, Group>(StringComparer.Ordinal);
        var seenEntry = false;
        foreach (var group in groups)
        {
            Group target;
            if (!seenEntry && group.Name == "Desktop Entry")
            {
                seenEntry = true;
                target = entry;
            }
            else if (group.Name.StartsWith("Desktop Action ", StringComparison.Ordinal))
            {
                target = actions[group.Name["Desktop Action ".Length..]] = new Group();
            }
            else
            {
                continue;
            }

            foreach (var line in group.Entries)
            {
                target.Set(line.Key, line.Suffix, line.Value, locale);
            }
        }

        if (entry.Hidden || string.IsNullOrEmpty(entry.Name.Value))
        {
            return null;
        }

        var attached = new List<DesktopAction>();
        foreach (var key in entry.Actions)
        {
            if (!actions.Remove(key, out var group))
            {
                Log.Debug($"{path}: action {key} has no [Desktop Action {key}] group, skipped");
                continue;
            }

            if (group.Name.Value is not { Length: > 0 } name)
            {
                Log.Debug($"{path}: action {key} has no Name, skipped");
                continue;
            }

            attached.Add(new DesktopAction(key, name, group.Icon, group.Exec));
        }

        foreach (var key in actions.Keys)
        {
            Log.Debug($"{path}: [Desktop Action {key}] is not in Actions=, ignored");
        }

        return new DesktopEntry
        {
            Id = id,
            Path = path,
            Name = entry.Name.Value!,
            Type = entry.Type,
            GenericName = entry.GenericName.Value,
            Comment = entry.Comment.Value,
            Icon = entry.Icon,
            Exec = entry.Exec,
            TryExec = entry.TryExec,
            WorkingDirectory = entry.WorkingDirectory,
            Terminal = entry.Terminal,
            NoDisplay = entry.NoDisplay,
            DBusActivatable = entry.DBusActivatable,
            StartupNotify = entry.StartupNotify,
            StartupWMClass = entry.StartupWMClass,
            Url = entry.Url,
            Categories = entry.Categories,
            Keywords = entry.Keywords.Value ?? [],
            MimeType = entry.MimeType,
            OnlyShowIn = entry.OnlyShowIn,
            NotShowIn = entry.NotShowIn,
            Implements = entry.Implements,
            Actions = attached,
        };
    }

    private sealed class Group
    {
        public Localised<string> Name;
        public Localised<string> GenericName;
        public Localised<string> Comment;
        public Localised<IReadOnlyList<string>> Keywords;
        public DesktopEntryType Type;
        public string? Icon;
        public string? Exec;
        public string? TryExec;
        public string? WorkingDirectory;
        public string? StartupWMClass;
        public string? Url;
        public bool Hidden;
        public bool Terminal;
        public bool NoDisplay;
        public bool DBusActivatable;
        public bool StartupNotify;
        public IReadOnlyList<string> Categories = [];
        public IReadOnlyList<string> MimeType = [];
        public IReadOnlyList<string> OnlyShowIn = [];
        public IReadOnlyList<string> NotShowIn = [];
        public IReadOnlyList<string> Implements = [];
        public IReadOnlyList<string> Actions = [];

        public void Set(ReadOnlySpan<char> key, string? suffix, ReadOnlySpan<char> value, DesktopLocale locale)
        {
            if (key.SequenceEqual("Name"))
            {
                Name.Offer(suffix, locale, value, static v => Unescape(v));
            }
            else if (key.SequenceEqual("GenericName"))
            {
                GenericName.Offer(suffix, locale, value, static v => Unescape(v));
            }
            else if (key.SequenceEqual("Comment"))
            {
                Comment.Offer(suffix, locale, value, static v => Unescape(v));
            }
            else if (key.SequenceEqual("Keywords"))
            {
                Keywords.Offer(suffix, locale, value, static v => SplitList(v));
            }
            else if (suffix is not null)
            {
                return;
            }
            else if (key.SequenceEqual("Type"))
            {
                Type = value switch
                {
                    "Application" => DesktopEntryType.Application,
                    "Link" => DesktopEntryType.Link,
                    "Directory" => DesktopEntryType.Directory,
                    _ => DesktopEntryType.Unknown,
                };
            }
            else if (key.SequenceEqual("Icon"))
            {
                Icon = Text(value);
            }
            else if (key.SequenceEqual("Exec"))
            {
                Exec = Text(value);
            }
            else if (key.SequenceEqual("TryExec"))
            {
                TryExec = Text(value);
            }
            else if (key.SequenceEqual("Path"))
            {
                WorkingDirectory = Text(value);
            }
            else if (key.SequenceEqual("StartupWMClass"))
            {
                StartupWMClass = Text(value);
            }
            else if (key.SequenceEqual("URL"))
            {
                Url = Text(value);
            }
            else if (key.SequenceEqual("Hidden"))
            {
                Hidden = Flag(value);
            }
            else if (key.SequenceEqual("Terminal"))
            {
                Terminal = Flag(value);
            }
            else if (key.SequenceEqual("NoDisplay"))
            {
                NoDisplay = Flag(value);
            }
            else if (key.SequenceEqual("DBusActivatable"))
            {
                DBusActivatable = Flag(value);
            }
            else if (key.SequenceEqual("StartupNotify"))
            {
                StartupNotify = Flag(value);
            }
            else if (key.SequenceEqual("Categories"))
            {
                Categories = SplitList(value);
            }
            else if (key.SequenceEqual("MimeType"))
            {
                MimeType = SplitList(value);
            }
            else if (key.SequenceEqual("OnlyShowIn"))
            {
                OnlyShowIn = SplitList(value);
            }
            else if (key.SequenceEqual("NotShowIn"))
            {
                NotShowIn = SplitList(value);
            }
            else if (key.SequenceEqual("Implements"))
            {
                Implements = SplitList(value);
            }
            else if (key.SequenceEqual("Actions"))
            {
                Actions = SplitList(value);
            }
        }

        private static string? Text(ReadOnlySpan<char> value) => value.Length == 0 ? null : Unescape(value);

        private static bool Flag(ReadOnlySpan<char> value) => value.SequenceEqual("true") || value.SequenceEqual("1");
    }

    private struct Localised<T>
        where T : class
    {
        public T? Value;
        private int _rank;

        public void Offer(string? suffix, DesktopLocale locale, ReadOnlySpan<char> raw, Func<string, T> convert)
        {
            var rank = suffix is null ? 0 : locale.Rank(suffix);
            if (suffix is not null && rank == 0)
            {
                return;
            }

            if (Value is null || rank > _rank)
            {
                Value = convert(raw.ToString());
                _rank = rank;
            }
        }
    }

    private static IReadOnlyList<string> SplitList(ReadOnlySpan<char> value)
    {
        var items = new List<string>();
        var builder = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                i++;
                builder.Append(value[i] == ';' ? ';' : Escaped(value[i]));
            }
            else if (c == ';')
            {
                if (builder.Length > 0)
                {
                    items.Add(builder.ToString());
                }

                builder.Clear();
            }
            else
            {
                builder.Append(c);
            }
        }

        if (builder.Length > 0)
        {
            items.Add(builder.ToString());
        }

        return items;
    }

    private static string Unescape(ReadOnlySpan<char> value)
    {
        if (value.IndexOf('\\') < 0)
        {
            return value.ToString();
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                builder.Append(c);
                continue;
            }

            i++;
            builder.Append(Escaped(value[i]));
        }

        return builder.ToString();
    }

    private static char Escaped(char c) => c switch
    {
        's' => ' ',
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        var other => other,
    };
}
