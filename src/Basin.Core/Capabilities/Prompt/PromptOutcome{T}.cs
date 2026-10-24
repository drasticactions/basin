namespace Basin.Capabilities;

/// <summary>
/// Holds the result of a portal dialog: the response code and, when the user accepted, the value that the user chose.
/// </summary>
/// <typeparam name="T">The type of the value that an accepted dialog returns.</typeparam>
/// <param name="Response">The response that the portal sends back to the frontend.</param>
/// <param name="Value">The chosen value. It is the default value unless <paramref name="Response"/> is <see cref="PromptResponse.Accepted"/>.</param>
public readonly record struct PromptOutcome<T>(PromptResponse Response, T? Value)
{
    /// <summary>
    /// Creates an outcome for a dialog that the user accepted.
    /// </summary>
    /// <param name="value">The value that the user chose.</param>
    /// <returns>An outcome with <see cref="PromptResponse.Accepted"/>.</returns>
    public static PromptOutcome<T> Accepted(T value) => new(PromptResponse.Accepted, value);

    /// <summary>
    /// Gets an outcome for a dialog that the user refused. The portal answers response code 1.
    /// </summary>
    public static PromptOutcome<T> Denied => new(PromptResponse.Denied, default);

    /// <summary>
    /// Gets an outcome for a dialog that the user dismissed or that the request closed. The portal answers response code 2.
    /// </summary>
    public static PromptOutcome<T> Cancelled => new(PromptResponse.Cancelled, default);

    /// <summary>
    /// Gets a value indicating whether the user accepted the dialog, so that <see cref="Value"/> holds a choice.
    /// </summary>
    public bool IsAccepted => Response == PromptResponse.Accepted;
}
