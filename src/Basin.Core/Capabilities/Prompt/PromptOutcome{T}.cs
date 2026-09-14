namespace Basin.Capabilities;

public readonly record struct PromptOutcome<T>(PromptResponse Response, T? Value)
{
    public static PromptOutcome<T> Accepted(T value) => new(PromptResponse.Accepted, value);

    public static PromptOutcome<T> Denied => new(PromptResponse.Denied, default);

    public static PromptOutcome<T> Cancelled => new(PromptResponse.Cancelled, default);

    public bool IsAccepted => Response == PromptResponse.Accepted;
}
