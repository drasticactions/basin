namespace BasinMcp;

internal sealed class McpGlob
{
    private readonly string _space;
    private readonly string _verb;

    private McpGlob(string text, string space, string verb)
    {
        Text = text;
        _space = space;
        _verb = verb;
    }

    public string Text { get; }

    public static McpGlob Parse(string pattern)
    {
        var slash = pattern.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == pattern.Length - 1 || pattern.IndexOf('/', slash + 1) >= 0)
        {
            throw new FormatException($"'{pattern}' is not namespace/verb; '*' matches within one part, for example outputs/*");
        }

        return new McpGlob(pattern, pattern[..slash], pattern[(slash + 1)..]);
    }

    public static IReadOnlyList<McpGlob> ParseList(string? patterns) =>
        string.IsNullOrWhiteSpace(patterns)
            ? []
            : [.. patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Parse)];

    public bool Matches(string method)
    {
        var slash = method.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && Part(_space, method.AsSpan(0, slash)) && Part(_verb, method.AsSpan(slash + 1));
    }

    private static bool Part(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text)
    {
        var star = pattern.IndexOf('*');
        if (star < 0)
        {
            return pattern.SequenceEqual(text);
        }

        var head = pattern[..star];
        if (!text.StartsWith(head))
        {
            return false;
        }

        var rest = pattern[(star + 1)..];
        for (var i = head.Length; i <= text.Length; i++)
        {
            if (Part(rest, text[i..]))
            {
                return true;
            }
        }

        return false;
    }
}
