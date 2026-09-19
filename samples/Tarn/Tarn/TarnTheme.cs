using Basin.Frames.Metacity;

namespace Tarn;

public static class TarnTheme
{
    private const string ResourceName = "tarn-metacity-theme-1.xml";

    public const string Name = "Tarn";

    public static MetacityTheme? Load(string name)
    {
        using var stream = typeof(TarnTheme).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the embedded theme '{ResourceName}' is missing from the sample");
        return MetacityTheme.Parse(stream, directory: ".", majorVersion: 1, name: Name);
    }
}
