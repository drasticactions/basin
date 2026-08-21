namespace Basin.Backend.Drm;

public readonly record struct EdidInfo(string Make, string Model, string Serial)
{
    public (double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy)? Chromaticities { get; init; }

    public bool SupportsPq { get; init; }

    public bool SupportsHlg { get; init; }

    public bool SupportsBt2020 { get; init; }

    public double MaxLuminance { get; init; }

    public double MaxFrameAverageLuminance { get; init; }

    public double MinLuminance { get; init; }

    public static EdidInfo Parse(ReadOnlySpan<byte> edid)
    {
        if (edid.Length < 128)
        {
            return new EdidInfo("unknown", "unknown", string.Empty);
        }

        var packed = (edid[8] << 8) | edid[9];
        Span<char> pnp =
        [
            (char)('A' - 1 + ((packed >> 10) & 0x1F)),
            (char)('A' - 1 + ((packed >> 5) & 0x1F)),
            (char)('A' - 1 + (packed & 0x1F)),
        ];
        var make = pnp[0] >= 'A' && pnp[0] <= 'Z' ? new string(pnp) : "unknown";

        var model = $"0x{edid[10] | (edid[11] << 8):X4}";
        var serial = string.Empty;
        var numericSerial = edid[12] | ((uint)edid[13] << 8) | ((uint)edid[14] << 16) | ((uint)edid[15] << 24);

        for (var offset = 54; offset + 18 <= 128; offset += 18)
        {
            if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 2] != 0)
            {
                continue;
            }

            var text = DescriptorText(edid.Slice(offset + 5, 13));
            switch (edid[offset + 3])
            {
                case 0xFC when text.Length > 0:
                    model = text;
                    break;
                case 0xFF when text.Length > 0:
                    serial = text;
                    break;
            }
        }

        if (serial.Length == 0 && numericSerial != 0)
        {
            serial = numericSerial.ToString();
        }

        var info = new EdidInfo(make, model, serial)
        {
            Chromaticities = ParseChromaticities(edid),
        };

        for (var block = 128; block + 128 <= edid.Length; block += 128)
        {
            if (edid[block] == 0x02)
            {
                info = ParseCtaBlock(edid.Slice(block, 128), info);
            }
        }

        return info;
    }

    private static (double, double, double, double, double, double, double, double)? ParseChromaticities(ReadOnlySpan<byte> edid)
    {
        var low = edid[25];
        var lowBw = edid[26];
        double C(int high, int lowBits) => ((high << 2) | lowBits) / 1024.0;
        var rx = C(edid[27], (low >> 6) & 3);
        var ry = C(edid[28], (low >> 4) & 3);
        var gx = C(edid[29], (low >> 2) & 3);
        var gy = C(edid[30], low & 3);
        var bx = C(edid[31], (lowBw >> 6) & 3);
        var by = C(edid[32], (lowBw >> 4) & 3);
        var wx = C(edid[33], (lowBw >> 2) & 3);
        var wy = C(edid[34], lowBw & 3);
        return ry <= 0 || gy <= 0 || by <= 0 || wy <= 0
            ? null
            : (rx, ry, gx, gy, bx, by, wx, wy);
    }

    private static EdidInfo ParseCtaBlock(ReadOnlySpan<byte> block, EdidInfo info)
    {
        int dtdOffset = block[2];
        if (dtdOffset < 4 || dtdOffset > 128)
        {
            return info;
        }

        var at = 4;
        while (at < dtdOffset)
        {
            var tag = block[at] >> 5;
            var length = block[at] & 0x1F;
            if (at + 1 + length > dtdOffset)
            {
                break;
            }

            if (tag == 7 && length >= 2)
            {
                var payload = block.Slice(at + 1, length);
                switch (payload[0])
                {
                    case 5:
                        info = info with { SupportsBt2020 = (payload[1] & 0x80) != 0 };
                        break;

                    case 6:
                        info = info with
                        {
                            SupportsPq = (payload[1] & 0x04) != 0,
                            SupportsHlg = (payload[1] & 0x08) != 0,
                        };
                        if (length >= 4 && payload[3] != 0)
                        {
                            info = info with { MaxLuminance = CtaLuminance(payload[3]) };
                        }

                        if (length >= 5 && payload[4] != 0)
                        {
                            info = info with { MaxFrameAverageLuminance = CtaLuminance(payload[4]) };
                        }

                        if (length >= 6 && payload[5] != 0 && info.MaxLuminance > 0)
                        {
                            var fraction = payload[5] / 255.0;
                            info = info with { MinLuminance = info.MaxLuminance * fraction * fraction / 100 };
                        }

                        break;
                }
            }

            at += 1 + length;
        }

        return info;
    }

    private static double CtaLuminance(byte code) => 50 * Math.Pow(2, code / 32.0);

    private static string DescriptorText(ReadOnlySpan<byte> raw)
    {
        var end = raw.IndexOf((byte)'\n');
        if (end < 0)
        {
            end = raw.Length;
        }

        return System.Text.Encoding.ASCII.GetString(raw[..end]).Trim();
    }
}
