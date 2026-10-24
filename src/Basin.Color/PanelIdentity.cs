namespace Basin.Color;

/// <summary>
/// The fields that a panel table entry matches on.
/// </summary>
/// <param name="Vendor">The three-letter EDID PNP vendor id, or an empty string without an EDID.</param>
/// <param name="Product">The EDID product code, or -1 without an EDID.</param>
/// <param name="Serial">The serial that the output reports.</param>
public readonly record struct PanelIdentity(string Vendor, int Product, string Serial)
{
    /// <summary>
    /// Reads the identity of an output from its EDID bytes and its serial.
    /// </summary>
    /// <param name="output">The output to identify.</param>
    /// <returns>The identity. An EDID shorter than 12 bytes gives an empty vendor and product -1.</returns>
    public static PanelIdentity Of(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var edid = output.EdidBytes.Span;
        if (edid.Length < 12)
        {
            return new PanelIdentity(string.Empty, -1, output.Serial);
        }

        var packed = (edid[8] << 8) | edid[9];
        var vendor = new string([
            (char)('A' - 1 + ((packed >> 10) & 0x1F)),
            (char)('A' - 1 + ((packed >> 5) & 0x1F)),
            (char)('A' - 1 + (packed & 0x1F)),
        ]);
        return new PanelIdentity(vendor, edid[10] | (edid[11] << 8), output.Serial);
    }
}
