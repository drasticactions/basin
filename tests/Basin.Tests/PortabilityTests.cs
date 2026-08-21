using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Basin.Tests;

public sealed class PortabilityTests
{
    public static TheoryData<string> RasterAssemblies => new() { "Basin.Render.Skia", "Basin.UI.Skia" };

    private static readonly string[] GpuAssemblies =
    [
        "Basin.Render.Gl",
        "Basin.Render.Vulkan",
        "Basin.Render.Gbm",
        "Basin.Render.Impeller",
        "Silk.NET.OpenGL",
        "Silk.NET.OpenGLES",
        "Silk.NET.Vulkan",
        "mesa-dotnet",
        "NImpeller",
    ];

    [Fact]
    public void The_suite_runs_on_the_transport_it_was_asked_for()
    {
        using var host = new CompositorTestHost();
        var managed = host.Display.Transport is Wayland.Server.ManagedTransport;
        Assert.Equal(CompositorTestHost.TransportUnderTest == Basin.Cli.TransportKind.Managed, managed);
    }

    [Theory]
    [MemberData(nameof(RasterAssemblies))]
    public void Raster_rows_reference_no_gpu_binding(string assembly)
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, $"{assembly}.dll"));
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.AssemblyReferences)
        {
            var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            Assert.False(
                Array.Exists(GpuAssemblies, gpu => name == gpu),
                $"{assembly} references {name}, which no host without a GPU binding can load");
        }
    }
}
