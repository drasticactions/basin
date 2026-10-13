using System.Runtime.CompilerServices;

namespace BasinGreeter.Tests;

internal static class Mute
{
    [ModuleInitializer]
    internal static void Everything()
    {
        Environment.SetEnvironmentVariable("BASIN_MUTE", "1");
        Environment.SetEnvironmentVariable("BASIN_GREETER_NO_POWER", "1");
        Environment.SetEnvironmentVariable("GREETD_SOCK", null);
    }
}
