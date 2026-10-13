namespace Basin.Color;

public readonly record struct PanelIdentity(string Vendor, int Product, string Serial)
{
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
