namespace Basin.Portal.Prompts.Skia;

/// <summary>
/// Shows Skia prompt views on the screen for <see cref="SkiaPortalPrompts"/>. The host owns the surfaces and routes input to the views.
/// </summary>
public interface ISkiaPromptHost
{
    /// <summary>
    /// Finds the output that a prompt for a parent window appears on.
    /// </summary>
    /// <param name="parentWindow">The portal parent window string. It can be empty.</param>
    /// <returns>The output, or <see langword="null"/> to use the first output of the layout.</returns>
    IOutput? OutputFor(string parentWindow);

    /// <summary>
    /// Shows a view on an output and paints it. The view stays owned by the caller.
    /// </summary>
    /// <param name="view">The view to show.</param>
    /// <param name="output">The output to show the view on.</param>
    /// <param name="cover"><see langword="true"/> to cover the whole output, as an area prompt needs. Otherwise the view is a centered dialog.</param>
    /// <returns>A surface that the caller disposes to hide the view, or <see langword="null"/> when the host cannot show it. The prompt is then cancelled.</returns>
    ISkiaPromptSurface? Show(SkiaPromptView view, IOutput output, bool cover);
}
