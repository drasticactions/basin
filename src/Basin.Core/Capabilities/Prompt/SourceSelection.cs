namespace Basin.Capabilities;

/// <summary>
/// Holds the answer the user gave to a <see cref="SourcePrompt"/>.
/// </summary>
/// <param name="Sources">The chosen sources, one stream each.</param>
/// <param name="PersistMode">The portal persist mode the user chose: 0 for none, 1 for this session, 2 until revoked. The caller clamps it to the requested mode.</param>
public readonly record struct SourceSelection(IReadOnlyList<SelectedSource> Sources, uint PersistMode);
