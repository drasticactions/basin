using Basin.Capabilities;

namespace Basin.Portal;

/// <summary>
/// Shows the prompt surfaces of a toolkit prompt implementation on the outputs of the compositor.
/// </summary>
public interface IPortalPromptHost
{
    /// <summary>
    /// Finds the output for a prompt from the parent window of the application.
    /// </summary>
    /// <param name="parentWindow">The parent window identifier from the frontend.</param>
    /// <returns>The output, or <see langword="null"/> to let the caller choose.</returns>
    IOutput? OutputFor(string parentWindow);

    /// <summary>
    /// Shows a prompt surface on an output.
    /// </summary>
    /// <param name="surface">The prompt surface.</param>
    /// <param name="output">The output to show it on.</param>
    void Show(IUISurface surface, IOutput output);

    /// <summary>
    /// Removes a prompt surface that <see cref="Show"/> showed.
    /// </summary>
    /// <param name="surface">The prompt surface.</param>
    void Hide(IUISurface surface);
}
