using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// The view model of one row in the screen share prompt.
/// </summary>
/// <param name="source">The source that the row selects.</param>
/// <param name="title">The output name or the window title.</param>
/// <param name="detail">The second line, for example the output mode or the app id.</param>
/// <param name="kind">The kind label, <c>monitor</c> or <c>window</c>.</param>
public sealed class SourceRowModel(SelectedSource source, string title, string detail, string kind)
{
    /// <summary>
    /// Gets the source that the row selects.
    /// </summary>
    public SelectedSource Source { get; } = source;

    /// <summary>
    /// Gets the output name or the window title.
    /// </summary>
    public string Title { get; } = title;

    /// <summary>
    /// Gets the second line of the row.
    /// </summary>
    public string Detail { get; } = detail;

    /// <summary>
    /// Gets the kind label, <c>monitor</c> or <c>window</c>.
    /// </summary>
    public string Kind { get; } = kind;
}
