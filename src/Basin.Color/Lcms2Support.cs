using Basin.Capabilities;
using Lcms2;
using Lcms2.Native;

namespace Basin.Color;

/// <summary>
/// Reports whether liblcms2 can load on this host.
/// </summary>
public static class Lcms2Support
{
    private static bool? _available;

    /// <summary>
    /// Gets a value indicating whether liblcms2 loads and is at least the version that the binding
    /// needs. The first read probes the library and caches the result.
    /// </summary>
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
