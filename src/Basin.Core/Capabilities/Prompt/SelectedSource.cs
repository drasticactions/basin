namespace Basin.Capabilities;

/// <summary>
/// Describes one source that the user chose in a source picker.
/// </summary>
/// <param name="Kind">The kind of the source. It holds exactly one flag.</param>
/// <param name="Output">The chosen output when <paramref name="Kind"/> is <see cref="PromptSourceKinds.Monitor"/>, otherwise <see langword="null"/>.</param>
/// <param name="ToplevelId">The chosen toplevel id when <paramref name="Kind"/> is <see cref="PromptSourceKinds.Window"/>, otherwise 0.</param>
/// <param name="VirtualWidth">The width in pixels of a virtual output, or 0 for the default.</param>
/// <param name="VirtualHeight">The height in pixels of a virtual output, or 0 for the default.</param>
public readonly record struct SelectedSource(PromptSourceKinds Kind, IOutput? Output, ulong ToplevelId, int VirtualWidth = 0, int VirtualHeight = 0);
