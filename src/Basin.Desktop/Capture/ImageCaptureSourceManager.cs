using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class ImageCaptureSourceManager : IDisposable
{
    public const int Version = 1;

    private static readonly Dictionary<nint, CaptureSource> ByResource = [];

    private readonly WlGlobal _outputGlobal;
    private readonly WlGlobal _toplevelGlobal;

    public ImageCaptureSourceManager(WlServerDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        _outputGlobal = display.CreateGlobal(ExtOutputImageCaptureSourceManagerV1.Interface, Version, OnBindOutput);
        _toplevelGlobal = display.CreateGlobal(ExtForeignToplevelImageCaptureSourceManagerV1.Interface, Version, OnBindToplevel);
    }

    public static CaptureSource FromResource(nint sourceResource) =>
        ByResource.GetValueOrDefault(sourceResource);

    public void Dispose()
    {
        _outputGlobal.Dispose();
        _toplevelGlobal.Dispose();
    }

    private static void Register(ExtImageCaptureSourceV1Resource resource, in CaptureSource source)
    {
        var handle = resource.RawHandle;
        ByResource[handle] = source;
        resource.Destroyed += (_, _) => ByResource.Remove(handle);
    }

    private static void OnBindOutput(WlClient client, uint version, uint id)
    {
        var manager = new ExtOutputImageCaptureSourceManagerV1Resource(client, version, id);
        manager.CreateSource += (_, e) =>
        {
            var resource = new ExtImageCaptureSourceV1Resource(client, manager.Version, e.Source);
            var output = OutputGlobal.FromResource(e.Output)?.Output;
            Register(resource, output is null ? default : CaptureSource.Output(output));
        };
    }

    private static void OnBindToplevel(WlClient client, uint version, uint id)
    {
        var manager = new ExtForeignToplevelImageCaptureSourceManagerV1Resource(client, version, id);
        manager.CreateSource += (_, e) =>
        {
            var resource = new ExtImageCaptureSourceV1Resource(client, manager.Version, e.Source);
            var toplevelId = e.ToplevelHandle is { } handle ? ForeignToplevelListManager.ToplevelOf(handle.RawHandle) : 0;
            Register(resource, toplevelId == 0 ? default : CaptureSource.Toplevel(toplevelId));
        };
    }
}
