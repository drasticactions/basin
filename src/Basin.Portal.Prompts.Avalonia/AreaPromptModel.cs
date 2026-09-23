using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

public sealed class AreaPromptModel : PromptModel
{
    public AreaPromptModel(in AreaPrompt prompt, int width, int height)
        : base(prompt.PickPoint ? "Pick a color" : "Select an area", prompt.AppId, prompt.DisplayName, prompt.IconPath)
    {
        PickPoint = prompt.PickPoint;
        Width = width;
        Height = height;
    }

    public bool PickPoint { get; }

    public int Width { get; }

    public int Height { get; }

    public string Hint => PickPoint ? "Click a pixel, Escape cancels" : "Drag to select, Escape cancels, Enter takes everything";

    public Box Selection { get; private set; }

    public void Choose(Box selection)
    {
        Selection = selection;
        Complete(PromptResponse.Accepted);
    }

    public void ChooseEverything() => Choose(new Box(0, 0, Width, Height));
}
