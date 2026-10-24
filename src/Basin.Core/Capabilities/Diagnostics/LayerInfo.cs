namespace Basin.Capabilities;

/// <summary>
/// One mapped layer-shell surface.
/// </summary>
/// <param name="Namespace">The namespace that the client set.</param>
/// <param name="Surface">The layer surface.</param>
/// <param name="Output">The name of the output of the surface, or <see langword="null"/> when it has no output.</param>
public readonly record struct LayerInfo(string Namespace, Surface Surface, string? Output);
