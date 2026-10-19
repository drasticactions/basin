using Basin.Frames.Metacity;

namespace Basin.Shell.Nested;

public static class NestedTheme
{
    private const string ResourceName = "basin-nested-metacity-theme-1.xml";

    public const string Name = "Nested";

    public static MetacityTheme? Load(string name) => Create(Name);

    public static MetacityTheme Create(string themeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(themeName);
        using var stream = typeof(NestedTheme).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the embedded theme '{ResourceName}' is missing");
        return MetacityTheme.Parse(stream, directory: ".", majorVersion: 1, name: themeName);
    }
}
