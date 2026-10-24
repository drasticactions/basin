namespace Basin;

/// <summary>
/// Reports whether a DRM output left its last frame lit at exit.
/// </summary>
/// <param name="Output">The connector name of the output.</param>
/// <param name="FramebufferId">The framebuffer ID that stays on the primary plane, or zero when nothing was read.</param>
/// <param name="Refusal">Why the output did not retain its frame, or <see langword="null"/> when it did.</param>
public readonly record struct HandoffRetention(string Output, uint FramebufferId, string? Refusal)
{
    /// <summary>
    /// Gets a value indicating whether the frame stays lit, which is when <see cref="Refusal"/> is <see langword="null"/>.
    /// </summary>
    public bool Retained => Refusal is null;
}
