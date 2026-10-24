namespace Basin;

/// <summary>
/// Describes one replacement of a lost renderer. The old stack is still alive while holders receive it, so a holder can release what it made on the old device.
/// </summary>
/// <param name="Old">The lost renderer and its allocator. They are disposed after every holder was told.</param>
/// <param name="New">The renderer and allocator that replace them.</param>
public sealed record RendererReplacement(RenderStack Old, RenderStack New);
