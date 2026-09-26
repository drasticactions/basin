using Basin;

namespace TinyComp;

internal sealed class StepTexture(string key)
{
    public string Key { get; } = key;

    public string Source { get; set; } = OverviewSetting.NoTexture;

    public string? Path { get; set; }

    public DateTime Stamp { get; set; }

    public string Name { get; set; } = OverviewSetting.NoTexture;

    public MemoryBuffer? Buffer { get; set; }

    public string? Failed { get; set; }
}
