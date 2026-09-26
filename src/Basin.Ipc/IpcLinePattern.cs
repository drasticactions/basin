using System.Globalization;
using System.Text.Json;

namespace Basin.Ipc;

internal sealed class IpcLinePattern
{
    private readonly Part[] _parts;

    private IpcLinePattern(string text, Part[] parts)
    {
        Text = text;
        _parts = parts;
    }

    public string Text { get; }

    public string Word => _parts[0].Literal!;

    public static IpcLinePattern Parse(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var words = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parts = new Part[words.Length];
        var optional = false;
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var isOptional = word.StartsWith('[') && word.EndsWith(']');
            if (isOptional)
            {
                word = word[1..^1];
                optional = true;
            }
            else if (optional)
            {
                throw new ArgumentException($"'{pattern}': a required word follows an optional one", nameof(pattern));
            }

            if (!(word.StartsWith('{') && word.EndsWith('}')))
            {
                if (isOptional)
                {
                    throw new ArgumentException($"'{pattern}': only a placeholder can be optional", nameof(pattern));
                }

                parts[i] = new Part(word, null, Kind.Literal, null, false);
                continue;
            }

            var inner = word[1..^1];
            var kind = Kind.String;
            string[]? choices = null;
            if (inner.EndsWith("...", StringComparison.Ordinal))
            {
                if (i != words.Length - 1)
                {
                    throw new ArgumentException($"'{pattern}': a rest placeholder comes last", nameof(pattern));
                }

                inner = inner[..^3];
                kind = Kind.Rest;
            }

            var colon = inner.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0)
            {
                var type = inner[(colon + 1)..];
                inner = inner[..colon];
                kind = type switch
                {
                    "int" => Kind.Int,
                    "number" => Kind.Number,
                    "bool" => Kind.Bool,
                    "string" => Kind.String,
                    _ when type.Contains('|', StringComparison.Ordinal) => Kind.Choice,
                    _ => throw new ArgumentException($"'{pattern}': unknown placeholder type '{type}'", nameof(pattern)),
                };
                if (kind == Kind.Choice)
                {
                    choices = type.Split('|');
                }
            }

            if (inner.Length == 0)
            {
                throw new ArgumentException($"'{pattern}': a placeholder needs a name", nameof(pattern));
            }

            parts[i] = new Part(null, inner, kind, choices, isOptional);
        }

        if (parts.Length == 0 || parts[0].Kind != Kind.Literal)
        {
            throw new ArgumentException($"'{pattern}': a line form starts with a word", nameof(pattern));
        }

        return new IpcLinePattern(pattern, parts);
    }

    public bool TryBind(IReadOnlyList<string> tokens, Utf8JsonWriter writer, out string? error)
    {
        error = null;
        var required = 0;
        foreach (var part in _parts)
        {
            if (!part.Optional)
            {
                required++;
            }
        }

        var rest = _parts[^1].Kind == Kind.Rest;
        if (tokens.Count < required || (!rest && tokens.Count > _parts.Length))
        {
            return false;
        }

        for (var i = 0; i < _parts.Length && i < tokens.Count; i++)
        {
            var part = _parts[i];
            if (part.Kind == Kind.Literal && !string.Equals(part.Literal, tokens[i], StringComparison.Ordinal))
            {
                return false;
            }

            if (part.Kind == Kind.Choice && Array.IndexOf(part.Choices!, tokens[i]) < 0)
            {
                return false;
            }
        }

        writer.WriteStartObject();
        for (var i = 0; i < _parts.Length && i < tokens.Count; i++)
        {
            var part = _parts[i];
            var token = tokens[i];
            switch (part.Kind)
            {
                case Kind.Literal:
                    break;
                case Kind.String:
                case Kind.Choice:
                    writer.WriteString(part.Name!, token);
                    break;
                case Kind.Rest:
                    writer.WriteString(part.Name!, string.Join(' ', tokens.Skip(i)));
                    break;
                case Kind.Int:
                    if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                    {
                        error = $"'{part.Name}' must be an integer, not '{token}'";
                        return false;
                    }

                    writer.WriteNumber(part.Name!, integer);
                    break;
                case Kind.Number:
                    if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                        || !double.IsFinite(number))
                    {
                        error = $"'{part.Name}' must be a number, not '{token}'";
                        return false;
                    }

                    writer.WriteNumber(part.Name!, number);
                    break;
                case Kind.Bool:
                    bool? flag = token switch
                    {
                        "1" or "true" or "on" or "yes" => true,
                        "0" or "false" or "off" or "no" => false,
                        _ => null,
                    };
                    if (flag is not { } value)
                    {
                        error = $"'{part.Name}' must be 1 or 0, not '{token}'";
                        return false;
                    }

                    writer.WriteBoolean(part.Name!, value);
                    break;
            }
        }

        writer.WriteEndObject();
        return true;
    }

    private enum Kind
    {
        Literal,
        String,
        Int,
        Number,
        Bool,
        Choice,
        Rest,
    }

    private readonly record struct Part(string? Literal, string? Name, Kind Kind, string[]? Choices, bool Optional);
}
