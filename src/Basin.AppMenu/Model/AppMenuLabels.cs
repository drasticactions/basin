namespace Basin.AppMenu;

/// <summary>Helpers for the labels that toolkits send.</summary>
public static class AppMenuLabels
{
    /// <summary>
    /// Removes the mnemonic markers that dbusmenu and GMenu put in labels. A single <c>_</c> is removed, and <c>__</c> becomes one underscore.
    /// </summary>
    /// <param name="label">The label as the client sent it.</param>
    /// <returns>The label without markers. It is the same instance when the label has no underscore.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
    public static string StripMnemonic(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (!label.Contains('_', StringComparison.Ordinal))
        {
            return label;
        }

        var result = new System.Text.StringBuilder(label.Length);
        for (var i = 0; i < label.Length; i++)
        {
            if (label[i] != '_')
            {
                result.Append(label[i]);
                continue;
            }

            if (i + 1 < label.Length && label[i + 1] == '_')
            {
                result.Append('_');
                i++;
            }
        }

        return result.ToString();
    }
}
