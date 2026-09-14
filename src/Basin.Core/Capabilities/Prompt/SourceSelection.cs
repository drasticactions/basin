namespace Basin.Capabilities;

public readonly record struct SourceSelection(IReadOnlyList<SelectedSource> Sources, uint PersistMode);
