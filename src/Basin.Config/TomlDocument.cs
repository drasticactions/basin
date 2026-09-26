using System.Text;
using Tomlyn;
using Tomlyn.Syntax;

namespace Basin.Config;

public sealed class TomlDocument
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private string _text;
    private DocumentSyntax _syntax;

    private TomlDocument(string text, DocumentSyntax syntax)
    {
        _text = text;
        _syntax = syntax;
    }

    public static TomlDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TomlDocument(text, ParseOrThrow(text));
    }

    public static TomlDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Parse(File.ReadAllText(path));
    }

    public override string ToString() => _text;

    public bool Contains(string table, string key) =>
        Scope(table) is { } scope && Find(scope.Items, key) is not null;

    public string? RawValue(string table, string key) =>
        Scope(table) is { } scope && Find(scope.Items, key) is { } entry ? Slice(entry.Value!) : null;

    public void Set(string table, string key, TomlValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (Scope(table) is not { } scope)
        {
            AppendAtEnd($"[{Header(table)}]\n{Line(key, value)}");
            return;
        }

        SetIn(scope, key, value);
    }

    public bool Remove(string table, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Scope(table) is { } scope && RemoveIn(scope, key);
    }

    public IReadOnlyList<string> Keys(string table)
    {
        var keys = new List<string>();
        if (Scope(table) is not { } scope)
        {
            return keys;
        }

        for (var i = 0; i < scope.Items.ChildrenCount; i++)
        {
            if (scope.Items.GetChild(i)!.Key is { } key && key.DotKeys.ChildrenCount == 0 && Segments(key.ToString()) is [var only])
            {
                keys.Add(only);
            }
        }

        return keys;
    }

    public IReadOnlyList<IReadOnlyList<string>> TableNames()
    {
        var names = new List<IReadOnlyList<string>>();
        foreach (var table in _syntax.Tables)
        {
            if (table.Kind == SyntaxKind.Table)
            {
                names.Add(Segments(table.Name!.ToString()));
            }
        }

        return names;
    }

    public int TableCount(string name)
    {
        var segments = Segments(name);
        var count = 0;
        foreach (var table in _syntax.Tables)
        {
            if (table.Kind == SyntaxKind.TableArray && Matches(table, segments))
            {
                count++;
            }
        }

        return count;
    }

    public bool ContainsInTable(string name, int index, string key) =>
        Find(ArrayScope(name, index).Items, key) is not null;

    public string? RawValueInTable(string name, int index, string key) =>
        Find(ArrayScope(name, index).Items, key) is { } entry ? Slice(entry.Value!) : null;

    public void SetInTable(string name, int index, string key, TomlValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        SetIn(ArrayScope(name, index), key, value);
    }

    public bool RemoveInTable(string name, int index, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return RemoveIn(ArrayScope(name, index), key);
    }

    public int AppendTable(string name)
    {
        var index = TableCount(name);
        AppendAtEnd($"[[{Header(name)}]]\n");
        return index;
    }

    public void RemoveTable(string name, int index)
    {
        var (start, end) = Block(ArrayTable(name, index));
        Replace(start, end, string.Empty);
    }

    public void MoveTable(string name, int from, int to)
    {
        var count = TableCount(name);
        if (from < 0 || from >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(from));
        }

        if (to < 0 || to >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(to));
        }

        if (from == to)
        {
            return;
        }

        var table = ArrayTable(name, from);
        var body = Slice(table.Span.Start.Offset, table.Span.End.Offset + 1);
        if (!body.EndsWith('\n'))
        {
            body += "\n";
        }

        var (start, end) = Block(table);
        var text = _text.Remove(start, end - start);
        var syntax = ParseOrThrow(text);
        var remaining = new List<TableSyntaxBase>();
        var segments = Segments(name);
        foreach (var candidate in syntax.Tables)
        {
            if (candidate.Kind == SyntaxKind.TableArray && Matches(candidate, segments))
            {
                remaining.Add(candidate);
            }
        }

        string moved;
        if (to < remaining.Count)
        {
            var at = LineStart(text, remaining[to].Span.Start.Offset);
            moved = text.Insert(at, body + "\n");
        }
        else
        {
            var last = remaining[^1];
            var at = Math.Min(text.Length, last.Span.End.Offset + 1);
            var prefix = at > 0 && text[at - 1] != '\n' ? "\n\n" : "\n";
            moved = text.Insert(at, prefix + body);
        }

        Commit(moved);
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var target = Path.GetFullPath(path);
        var info = new FileInfo(target);
        if (info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } resolved)
        {
            target = resolved.FullName;
        }

        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        UnixFileMode? mode = !OperatingSystem.IsWindows() && File.Exists(target) ? File.GetUnixFileMode(target) : null;
        var temporary = target + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Utf8.GetBytes(_text));
            stream.Flush(flushToDisk: true);
        }

        if (mode is { } keep && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, keep);
        }

        File.Move(temporary, target, overwrite: true);
    }

    public static IReadOnlyList<string> Segments(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var segments = new List<string>();
        var i = 0;
        while (i < name.Length)
        {
            while (i < name.Length && name[i] is ' ' or '\t')
            {
                i++;
            }

            if (i >= name.Length)
            {
                break;
            }

            var builder = new StringBuilder();
            if (name[i] is '"' or '\'')
            {
                var quote = name[i++];
                while (i < name.Length && name[i] != quote)
                {
                    if (quote == '"' && name[i] == '\\' && i + 1 < name.Length)
                    {
                        i++;
                        builder.Append(name[i] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            'r' => '\r',
                            'b' => '\b',
                            'f' => '\f',
                            _ => name[i],
                        });
                    }
                    else
                    {
                        builder.Append(name[i]);
                    }

                    i++;
                }

                i++;
            }
            else
            {
                while (i < name.Length && name[i] is not '.' and not ' ' and not '\t')
                {
                    builder.Append(name[i++]);
                }
            }

            segments.Add(builder.ToString());
            while (i < name.Length && name[i] is ' ' or '\t')
            {
                i++;
            }

            if (i < name.Length && name[i] == '.')
            {
                i++;
            }
        }

        return segments;
    }

    private readonly record struct TableScope(SyntaxList<KeyValueSyntax> Items, int InsertAt);

    private TableScope? Scope(string table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var segments = Segments(table);
        if (segments.Count == 0)
        {
            var root = _syntax.KeyValues;
            return new TableScope(root, root.ChildrenCount > 0 ? AfterLast(root) : 0);
        }

        foreach (var candidate in _syntax.Tables)
        {
            if (candidate.Kind == SyntaxKind.Table && Matches(candidate, segments))
            {
                return ScopeOf(candidate);
            }
        }

        return null;
    }

    private TableScope ArrayScope(string name, int index) => ScopeOf(ArrayTable(name, index));

    private TableSyntaxBase ArrayTable(string name, int index)
    {
        ArgumentNullException.ThrowIfNull(name);
        var segments = Segments(name);
        var seen = 0;
        foreach (var candidate in _syntax.Tables)
        {
            if (candidate.Kind == SyntaxKind.TableArray && Matches(candidate, segments) && seen++ == index)
            {
                return candidate;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(index), $"there is no [[{name}]] number {index}");
    }

    private TableScope ScopeOf(TableSyntaxBase table)
    {
        if (table.Items.ChildrenCount > 0)
        {
            return new TableScope(table.Items, AfterLast(table.Items));
        }

        var headerEnd = _text.IndexOf('\n', table.Name!.Span.End.Offset);
        return new TableScope(table.Items, headerEnd < 0 ? _text.Length : headerEnd + 1);
    }

    private static int AfterLast(SyntaxList<KeyValueSyntax> items) =>
        items.GetChild(items.ChildrenCount - 1)!.Span.End.Offset + 1;

    private static bool Matches(TableSyntaxBase table, IReadOnlyList<string> segments)
    {
        var name = Segments(table.Name!.ToString());
        if (name.Count != segments.Count)
        {
            return false;
        }

        for (var i = 0; i < name.Count; i++)
        {
            if (!string.Equals(name[i], segments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static KeyValueSyntax? Find(SyntaxList<KeyValueSyntax> items, string key)
    {
        for (var i = 0; i < items.ChildrenCount; i++)
        {
            var entry = items.GetChild(i)!;
            if (entry.Key is { } syntax && syntax.DotKeys.ChildrenCount == 0 &&
                Segments(syntax.ToString()) is [var only] && string.Equals(only, key, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    private void SetIn(TableScope scope, string key, TomlValue value)
    {
        if (Find(scope.Items, key) is { } entry)
        {
            var node = entry.Value!;
            Replace(node.Span.Start.Offset, node.Span.End.Offset + 1, value.Text);
            return;
        }

        var at = Math.Min(scope.InsertAt, _text.Length);
        var prefix = at > 0 && _text[at - 1] != '\n' ? "\n" : string.Empty;
        Replace(at, at, prefix + Line(key, value));
    }

    private bool RemoveIn(TableScope scope, string key)
    {
        if (Find(scope.Items, key) is not { } entry)
        {
            return false;
        }

        var start = LineStart(_text, entry.Span.Start.Offset);
        var end = Math.Min(_text.Length, entry.Span.End.Offset + 1);
        Replace(start, end, string.Empty);
        return true;
    }

    private (int Start, int End) Block(TableSyntaxBase table)
    {
        var start = LineStart(_text, table.Span.Start.Offset);
        var end = Math.Min(_text.Length, table.Span.End.Offset + 1);
        var blank = end;
        while (blank < _text.Length && _text[blank] is ' ' or '\t' or '\r')
        {
            blank++;
        }

        if (blank < _text.Length && _text[blank] == '\n')
        {
            end = blank + 1;
        }
        else if (blank == _text.Length)
        {
            end = blank;
        }

        return (start, end);
    }

    private static int LineStart(string text, int offset)
    {
        var start = offset;
        while (start > 0 && text[start - 1] is ' ' or '\t')
        {
            start--;
        }

        return start == 0 || text[start - 1] == '\n' ? start : offset;
    }

    private void AppendAtEnd(string block)
    {
        var prefix = _text.Length == 0 ? string.Empty : _text.EndsWith('\n') ? "\n" : "\n\n";
        Replace(_text.Length, _text.Length, prefix + block);
    }

    private void Replace(int start, int end, string replacement) =>
        Commit(string.Concat(_text.AsSpan(0, start), replacement, _text.AsSpan(end)));

    private void Commit(string text)
    {
        var syntax = Toml.Parse(text);
        if (syntax.HasErrors)
        {
            throw new InvalidOperationException($"the edit would not parse: {First(syntax)}");
        }

        try
        {
            _ = Toml.ToModel(syntax);
        }
        catch (Exception error) when (error is TomlException or InvalidCastException or InvalidOperationException)
        {
            throw new InvalidOperationException($"the edit would define a key twice: {error.Message}", error);
        }

        _text = text;
        _syntax = syntax;
    }

    private string Slice(SyntaxNode node) => Slice(node.Span.Start.Offset, node.Span.End.Offset + 1);

    private string Slice(int start, int end) => _text[start..Math.Min(end, _text.Length)];

    private static string Line(string key, TomlValue value) => $"{TomlValue.Key(key)} = {value.Text}\n";

    private static string Header(string table)
    {
        var segments = Segments(table);
        var builder = new StringBuilder();
        for (var i = 0; i < segments.Count; i++)
        {
            builder.Append(i == 0 ? string.Empty : ".").Append(TomlValue.Key(segments[i]));
        }

        return builder.ToString();
    }

    private static DocumentSyntax ParseOrThrow(string text)
    {
        var syntax = Toml.Parse(text);
        if (syntax.HasErrors)
        {
            throw new FormatException(First(syntax));
        }

        return syntax;
    }

    private static string First(DocumentSyntax syntax)
    {
        foreach (var message in syntax.Diagnostics)
        {
            return message.ToString();
        }

        return "unknown error";
    }
}
