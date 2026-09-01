namespace DeskbarWm;

internal sealed record WindowEntry(ManagedWindow Window, string Label, bool Hidden, bool Active) : BarRow;
