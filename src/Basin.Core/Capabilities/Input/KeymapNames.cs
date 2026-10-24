namespace Basin.Capabilities;

/// <summary>
/// The XKB rule names that select a keymap. A <see langword="null"/> or empty part uses the XKB default.
/// </summary>
/// <param name="Rules">The XKB rules file, for example <c>evdev</c>.</param>
/// <param name="Model">The keyboard model, for example <c>pc105</c>.</param>
/// <param name="Layout">The layout, for example <c>us</c>, or a comma-separated list of layouts.</param>
/// <param name="Variant">The layout variant, for example <c>dvorak</c>.</param>
/// <param name="Options">The XKB options, for example <c>ctrl:nocaps</c>.</param>
public readonly record struct KeymapNames(
    string? Rules = null,
    string? Model = null,
    string? Layout = null,
    string? Variant = null,
    string? Options = null)
{
    /// <summary>
    /// Parses a layout identifier of the form <c>layout</c> or <c>layout(variant)</c>, for example <c>de(nodeadkeys)</c>.
    /// </summary>
    /// <param name="id">The layout identifier.</param>
    /// <returns>Names with only <see cref="Layout"/> and <see cref="Variant"/> set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
    public static KeymapNames Parse(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var open = id.IndexOf('(', StringComparison.Ordinal);
        if (open > 0 && id.EndsWith(')'))
        {
            return new KeymapNames(Layout: id[..open].Trim(), Variant: id[(open + 1)..^1].Trim());
        }

        return new KeymapNames(Layout: id.Trim());
    }

    /// <summary>
    /// Gives a <c>XKB_DEFAULT_*=value</c> line for each part that is not empty, for a session environment.
    /// </summary>
    /// <returns>The lines in the order rules, model, layout, variant, options.</returns>
    public IEnumerable<string> EnvironmentLines()
    {
        if (Rules is { Length: > 0 } rules)
        {
            yield return "XKB_DEFAULT_RULES=" + rules;
        }

        if (Model is { Length: > 0 } model)
        {
            yield return "XKB_DEFAULT_MODEL=" + model;
        }

        if (Layout is { Length: > 0 } layout)
        {
            yield return "XKB_DEFAULT_LAYOUT=" + layout;
        }

        if (Variant is { Length: > 0 } variant)
        {
            yield return "XKB_DEFAULT_VARIANT=" + variant;
        }

        if (Options is { Length: > 0 } options)
        {
            yield return "XKB_DEFAULT_OPTIONS=" + options;
        }
    }
}
