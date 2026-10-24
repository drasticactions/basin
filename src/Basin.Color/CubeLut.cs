using System.Globalization;
using System.Text;

namespace Basin.Color;

/// <summary>
/// Reads Adobe and Resolve <c>.cube</c> files as 3D color lookup tables.
/// </summary>
public static class CubeLut
{
    /// <summary>
    /// The largest grid edge that <see cref="Parse"/> accepts.
    /// </summary>
    public const int MaxSize = 128;

    /// <summary>
    /// Parses a 3D <c>.cube</c> file. A domain other than 0 to 1 is folded into the table, so the
    /// result always samples from 0 to 1.
    /// </summary>
    /// <param name="file">The UTF-8 file contents.</param>
    /// <returns>A new table with red varying fastest.</returns>
    /// <exception cref="FormatException">The file is a 1D LUT, has no size, has the wrong row count, or has a bad number or domain.</exception>
    public static ColorLut3D Parse(ReadOnlySpan<byte> file)
    {
        var text = Encoding.UTF8.GetString(file);
        var size = 0;
        var domainMin = (R: 0f, G: 0f, B: 0f);
        var domainMax = (R: 1f, G: 1f, B: 1f);
        float[]? data = null;
        var count = 0;
        var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (fields[0])
            {
                case "TITLE":
                    continue;
                case "LUT_1D_SIZE":
                case "LUT_1D_INPUT_RANGE":
                    throw new FormatException($"line {lineNumber}: a 1D LUT ({fields[0]}) is not a look; only LUT_3D_SIZE is read");
                case "LUT_3D_INPUT_RANGE":
                    var range = Triple(fields, lineNumber, 2);
                    domainMin = (range.R, range.R, range.R);
                    domainMax = (range.G, range.G, range.G);
                    continue;
                case "LUT_3D_SIZE":
                    if (fields.Length != 2 || !int.TryParse(fields[1], CultureInfo.InvariantCulture, out size) ||
                        size < 2 || size > MaxSize)
                    {
                        throw new FormatException($"line {lineNumber}: LUT_3D_SIZE must be an integer from 2 to {MaxSize}");
                    }

                    data = new float[size * size * size * 3];
                    continue;
                case "DOMAIN_MIN":
                    domainMin = Triple(fields, lineNumber, 3);
                    continue;
                case "DOMAIN_MAX":
                    domainMax = Triple(fields, lineNumber, 3);
                    continue;
            }

            if (data is null)
            {
                if (char.IsLetter(fields[0][0]))
                {
                    continue;
                }

                throw new FormatException($"line {lineNumber}: a data row comes before LUT_3D_SIZE");
            }

            if (count * 3 >= data.Length)
            {
                throw new FormatException($"line {lineNumber}: more rows than LUT_3D_SIZE {size} allows");
            }

            var row = Triple(fields, lineNumber, 3);
            data[count * 3] = row.R;
            data[(count * 3) + 1] = row.G;
            data[(count * 3) + 2] = row.B;
            count++;
        }

        if (data is null)
        {
            throw new FormatException("no LUT_3D_SIZE line");
        }

        if (count != size * size * size)
        {
            throw new FormatException($"{count} rows where LUT_3D_SIZE {size} needs {size * size * size}");
        }

        var table = new ColorLut3D(size, data);
        if (domainMin == (0f, 0f, 0f) && domainMax == (1f, 1f, 1f))
        {
            return table;
        }

        if (domainMax.R <= domainMin.R || domainMax.G <= domainMin.G || domainMax.B <= domainMin.B)
        {
            throw new FormatException("DOMAIN_MAX must exceed DOMAIN_MIN on every channel");
        }

        var folded = new float[data.Length];
        var index = 0;
        for (var b = 0; b < size; b++)
        {
            for (var g = 0; g < size; g++)
            {
                for (var r = 0; r < size; r++)
                {
                    var (fr, fg, fb) = table.SampleTetrahedral(
                        Fold(r / (float)(size - 1), domainMin.R, domainMax.R),
                        Fold(g / (float)(size - 1), domainMin.G, domainMax.G),
                        Fold(b / (float)(size - 1), domainMin.B, domainMax.B));
                    folded[index++] = fr;
                    folded[index++] = fg;
                    folded[index++] = fb;
                }
            }
        }

        return new ColorLut3D(size, folded);
    }

    private static float Fold(float value, float min, float max) => Math.Clamp((value - min) / (max - min), 0, 1);

    private static (float R, float G, float B) Triple(string[] fields, int lineNumber, int expected)
    {
        var offset = fields.Length == expected + 1 && char.IsLetter(fields[0][0]) ? 1 : 0;
        if (fields.Length - offset != expected)
        {
            throw new FormatException($"line {lineNumber}: expected {expected} numbers");
        }

        var values = new float[3];
        for (var i = 0; i < expected; i++)
        {
            if (!float.TryParse(fields[offset + i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                throw new FormatException($"line {lineNumber}: '{fields[offset + i]}' is not a number");
            }
        }

        return (values[0], values[1], values[2]);
    }
}
