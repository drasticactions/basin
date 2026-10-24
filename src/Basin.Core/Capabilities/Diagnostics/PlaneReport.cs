namespace Basin.Capabilities;

/// <summary>
/// The plane offload state of one output after a frame.
/// </summary>
/// <param name="Output">The output name.</param>
/// <param name="Skipped">The reason that the output did not try offload for the frame, or <see langword="null"/> when it tried.</param>
/// <param name="Entries">One decision for each candidate node: the nodes on a plane first, then the declined nodes.</param>
public sealed record PlaneReport(string Output, string? Skipped, IReadOnlyList<PlaneDecision> Entries)
{
    /// <summary>
    /// Compares this report with another by value, entries included.
    /// </summary>
    /// <param name="other">The report to compare with, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="other"/> has the same output, skip reason and entries.</returns>
    public bool SameAs(PlaneReport? other) =>
        other is not null && other.Output == Output && other.Skipped == Skipped && other.Entries.SequenceEqual(Entries);
}
