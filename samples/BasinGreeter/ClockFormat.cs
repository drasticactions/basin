using System.Globalization;
using System.Text;

namespace BasinGreeter;

internal static class ClockFormat
{
    public static string Format(string pattern, DateTime now)
    {
        var text = new StringBuilder(pattern.Length + 8);
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '%' || i + 1 >= pattern.Length)
            {
                text.Append(pattern[i]);
                continue;
            }

            var code = pattern[++i];
            text.Append(code switch
            {
                'H' => now.ToString("HH", CultureInfo.InvariantCulture),
                'M' => now.ToString("mm", CultureInfo.InvariantCulture),
                'S' => now.ToString("ss", CultureInfo.InvariantCulture),
                'I' => now.ToString("hh", CultureInfo.InvariantCulture),
                'p' => now.ToString("tt", CultureInfo.InvariantCulture),
                'a' => now.ToString("ddd", CultureInfo.InvariantCulture),
                'A' => now.ToString("dddd", CultureInfo.InvariantCulture),
                'b' => now.ToString("MMM", CultureInfo.InvariantCulture),
                'B' => now.ToString("MMMM", CultureInfo.InvariantCulture),
                'd' => now.ToString("dd", CultureInfo.InvariantCulture),
                'e' => now.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2),
                'm' => now.ToString("MM", CultureInfo.InvariantCulture),
                'Y' => now.ToString("yyyy", CultureInfo.InvariantCulture),
                '%' => "%",
                _ => "%" + code,
            });
        }

        return text.ToString();
    }

    public static bool PerSecond(string pattern) => pattern.Contains("%S", StringComparison.Ordinal);
}
