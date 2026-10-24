using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// The view model of an area prompt, which covers one output and asks for a rectangle or a pixel.
/// </summary>
public sealed class AreaPromptModel : PromptModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AreaPromptModel"/> class.
    /// </summary>
    /// <param name="prompt">The request. Its <see cref="AreaPrompt.PickPoint"/> selects a one-pixel pick.</param>
    /// <param name="width">The width of the output in logical pixels.</param>
    /// <param name="height">The height of the output in logical pixels.</param>
    public AreaPromptModel(in AreaPrompt prompt, int width, int height)
        : base(prompt.PickPoint ? "Pick a color" : "Select an area", prompt.AppId, prompt.DisplayName, prompt.IconPath)
    {
        PickPoint = prompt.PickPoint;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets a value indicating whether the prompt picks one pixel for a color, not an area.
    /// </summary>
    public bool PickPoint { get; }

    /// <summary>
    /// Gets the width of the output in logical pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the output in logical pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the instruction text that the view shows at the top.
    /// </summary>
    public string Hint => PickPoint ? "Click a pixel, Escape cancels" : "Drag to select, Escape cancels, Enter takes everything";

    /// <summary>
    /// Gets the chosen area in output-local logical pixels. It is empty until <see cref="Choose"/> is called.
    /// </summary>
    public Box Selection { get; private set; }

    /// <summary>
    /// Sets the selection and completes the prompt with an accepted response.
    /// </summary>
    /// <param name="selection">The area in output-local logical pixels.</param>
    public void Choose(Box selection)
    {
        Selection = selection;
        Complete(PromptResponse.Accepted);
    }

    /// <summary>
    /// Selects the whole output and completes the prompt with an accepted response.
    /// </summary>
    public void ChooseEverything() => Choose(new Box(0, 0, Width, Height));
}
