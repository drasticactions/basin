using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

public sealed class SourceRowModel(SelectedSource source, string title, string detail, string kind)
{
    public SelectedSource Source { get; } = source;

    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public string Kind { get; } = kind;
}
