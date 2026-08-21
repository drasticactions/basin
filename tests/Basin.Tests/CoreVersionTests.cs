using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Basin.Tests;

public sealed class CoreVersionTests
{
    [Fact]
    public void No_core_global_advertises_past_what_the_wayland_submodule_declares()
    {
        var declared = DeclaredVersions();
        using var host = new CompositorTestHost();

        var advertised = new List<(string Interface, uint Version)>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) => advertised.Add((e.Interface, e.Version));
        host.PumpToClient();

        var checkedAny = false;
        foreach (var (name, version) in advertised)
        {
            if (!declared.TryGetValue(name, out var max))
            {
                continue;
            }

            checkedAny = true;
            Assert.True(
                version <= max,
                $"{name} is advertised at {version}, but wayland declares only {max}");
        }

        Assert.True(checkedAny, "no core global was checked — is the wayland submodule checked out?");
    }

    [Fact]
    public void The_core_globals_basin_owns_are_all_measured()
    {
        var declared = DeclaredVersions();
        foreach (var name in (string[])
            ["wl_compositor", "wl_subcompositor", "wl_shm", "wl_output", "wl_seat",
             "wl_data_device_manager", "wl_fixes"])
        {
            Assert.True(declared.ContainsKey(name), $"{name} is missing from the wayland submodule's XML");
        }
    }

    private static Dictionary<string, uint> DeclaredVersions([CallerFilePath] string sourcePath = "")
    {
        var xml = Path.Combine(
            Path.GetDirectoryName(sourcePath)!, "..", "..", "external", "wayland", "protocol", "wayland.xml");
        Assert.True(
            File.Exists(xml),
            $"wayland.xml not found at {xml} — run 'git submodule update --init --recursive'");

        return XDocument.Load(xml).Root!
            .Elements("interface")
            .ToDictionary(
                e => (string)e.Attribute("name")!,
                e => uint.Parse((string)e.Attribute("version")!));
    }
}
