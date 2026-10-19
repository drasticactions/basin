using Basin.Frames.Metacity;
using Basin.Shell.Nested;

namespace PortComp;

public static class PortCompTheme
{
    public const string Name = "PortComp";

    public static MetacityTheme? Load(string name) => NestedTheme.Create(Name);
}
