using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// The view model of a yes or no question with Deny and Allow buttons.
/// </summary>
public sealed class ConfirmPromptModel : PromptModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmPromptModel"/> class.
    /// </summary>
    /// <param name="prompt">The request that supplies the title and the body text.</param>
    public ConfirmPromptModel(in ConfirmPrompt prompt)
        : base(prompt.Title, prompt.AppId, prompt.DisplayName, prompt.IconPath) => Body = prompt.Body;

    /// <summary>
    /// Gets the question text. It can hold more than one line.
    /// </summary>
    public string Body { get; }
}
