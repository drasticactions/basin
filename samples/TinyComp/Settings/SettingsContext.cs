using Basin.UI.Paper;

namespace TinyComp;

internal sealed class SettingsContext
{
    public required IReadOnlyList<string> Renderers { get; init; }

    public required IReadOnlyList<string> MetacityThemes { get; init; }

    public required IReadOnlyCollection<string> FromFlags { get; init; }

    public required IReadOnlyList<SettingsOutput> Outputs { get; init; }

    public required ChordCapture Capture { get; init; }

    public required Prowl.Scribe.FontFile Font { get; init; }

    public Func<string, IReadOnlyList<string>>? ShaderParameters { get; init; }

    public string? ConfigPath { get; init; }

    public Action? Close { get; init; }

    public Action? Save { get; init; }

    public Action? Overwrite { get; init; }

    public Action? Revert { get; init; }

    public Action? DragStart { get; init; }
}
