namespace Basin.SoftKeyboard.Ipc;

/// <summary>
/// Holds the shell callbacks that the soft keyboard IPC methods call. The shell owns the policy of when the keyboard shows.
/// </summary>
public sealed record SoftKeyboardIpcHooks
{
    /// <summary>
    /// Gets the method prefix, for example the shell name. Each method registers as this prefix plus "/keyboard", "/keyboard-tap" and so on.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Gets the callback that tells whether the keyboard shows now. The tap, type and hold methods refuse their work when it returns <see langword="false"/>.
    /// </summary>
    public required Func<bool> IsShown { get; init; }

    /// <summary>
    /// Gets the callback that raises the keyboard for the "show" state.
    /// </summary>
    public required Action Show { get; init; }

    /// <summary>
    /// Gets the callback that hides the keyboard. Its argument is the reason, which is "command" for an IPC request.
    /// </summary>
    public required Action<string> Hide { get; init; }

    /// <summary>
    /// Gets the callback that shows a hidden keyboard or hides a shown one.
    /// </summary>
    public required Action Toggle { get; init; }

    /// <summary>
    /// Gets the callback that prints the shell's KEYBOARD line. The keyboard method calls it when no state is given.
    /// </summary>
    public required Action Report { get; init; }
}
