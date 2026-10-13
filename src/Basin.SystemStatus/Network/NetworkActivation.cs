namespace Basin.SystemStatus;

public sealed record NetworkActivation(bool Succeeded, string? Error)
{
    public static NetworkActivation Success { get; } = new(true, null);

    public static NetworkActivation Failed(string error) => new(false, error);
}
