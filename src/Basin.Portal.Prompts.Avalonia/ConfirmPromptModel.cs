using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

public sealed class ConfirmPromptModel : PromptModel
{
    public ConfirmPromptModel(in ConfirmPrompt prompt)
        : base(prompt.Title, prompt.AppId, prompt.DisplayName, prompt.IconPath) => Body = prompt.Body;

    public string Body { get; }
}
