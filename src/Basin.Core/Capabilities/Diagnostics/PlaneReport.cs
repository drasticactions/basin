namespace Basin.Capabilities;

public sealed record PlaneReport(string Output, string? Skipped, IReadOnlyList<PlaneDecision> Entries)
{
    public bool SameAs(PlaneReport? other) =>
        other is not null && other.Output == Output && other.Skipped == Skipped && other.Entries.SequenceEqual(Entries);
}
