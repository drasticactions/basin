namespace Basin.SoftKeyboard.Ipc;

public sealed record SoftKeyboardIpcHooks
{
    public required string Namespace { get; init; }

    public required Func<bool> IsShown { get; init; }

    public required Action Show { get; init; }

    public required Action<string> Hide { get; init; }

    public required Action Toggle { get; init; }

    public required Action Report { get; init; }
}
