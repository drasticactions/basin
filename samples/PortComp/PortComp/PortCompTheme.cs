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
            Log.Info($"theme '{name}' not found; using the built-in '{Name}' theme");
        }

        return NestedTheme.Create(Name);
    }
}
