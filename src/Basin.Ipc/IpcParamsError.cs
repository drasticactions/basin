using System.Text.Json;

namespace Basin.Ipc;

internal static class IpcParamsError
{
    private const string Missing = "missing required properties including: ";

    public static string Describe(JsonException exception)
    {
        var message = exception.Message;
        var missing = message.IndexOf(Missing, StringComparison.Ordinal);
        if (missing >= 0)
        {
            return $"{message[(missing + Missing.Length)..].TrimEnd('.')} is required";
        }

        var key = exception.Path is { Length: > 2 } path && path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : null;
        if (exception.InnerException is null && !message.StartsWith("The JSON value", StringComparison.Ordinal)
            && !message.StartsWith("'$", StringComparison.Ordinal))
        {
            var cut = message.IndexOf(" Path: ", StringComparison.Ordinal);
            return cut > 0 ? message[..cut] : message;
        }

        return key is null ? "'params' is not the shape this method takes" : $"'{key}' has the wrong type or is out of range";
    }
}
