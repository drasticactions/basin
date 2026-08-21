using Basin.Capabilities;
using Lcms2;
using Lcms2.Native;

namespace Basin.Color;

public static class Lcms2Support
{
    private static bool? _available;

    public static bool IsAvailable => _available ??= Probe();

    private static bool Probe()
    {
        try
        {
            return Lcms2Library.IsAvailable && Lcms2Library.Version >= Lcms2Library.BindingVersion;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
