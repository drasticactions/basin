using Basin.Diagnostics;
using Basin.Frames.Metacity;
using Basin.Shell.Nested;

namespace PortComp;

public static class PortCompTheme
{
    public const string Name = "PortComp";

    private static readonly BasinLogger Log = BasinLog.For("portcomp");

    public static MetacityTheme? Load(string name)
    {
        if (name != Name)
        {
            Log.Warn($"theme: {name} not found, built-in {Name} theme used");
        }

        return NestedTheme.Create(Name);
    }
}
