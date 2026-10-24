using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the output and foreign-toplevel image capture source globals. It maps each source resource to the <see cref="CaptureSource"/> that it names.
/// </summary>
public sealed class ImageCaptureSourceManager : IDisposable, IExtOutputImageCaptureSourceManagerV1Handler, IExtForeignToplevelImageCaptureSourceManagerV1Handler
{
    /// <summary>
    /// The protocol version that both globals advertise.
    /// </summary>
    public const int Version = 1;

    private static readonly Dictionary<nint, CaptureSource> ByResource = [];

    private readonly WlGlobal _outputGlobal;
    private readonly WlGlobal _toplevelGlobal;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCaptureSourceManager"/> class and creates both globals.
    /// </summary>
    /// <param name="display">The display that holds the globals.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public ImageCaptureSourceManager(WlServerDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        _outputGlobal = display.CreateGlobal(ExtOutputImageCaptureSourceManagerV1.Interface, Version, OnBindOutput);
        _toplevelGlobal = display.CreateGlobal(ExtForeignToplevelImageCaptureSourceManagerV1.Interface, Version, OnBindToplevel);
    }

    /// <summary>
    /// Finds the capture source that a live <c>ext_image_capture_source_v1</c> resource names.
    /// </summary>
    /// <param name="sourceResource">The raw handle of the source resource.</param>
    /// <returns>The source, or the default value with <see cref="CaptureSourceKind.None"/> when the handle is unknown or destroyed.</returns>
    public static CaptureSource FromResource(nint sourceResource) =>
        ByResource.GetValueOrDefault(sourceResource);

    /// <summary>
    /// Removes both globals. Source resources that clients hold stay valid until they are destroyed.
    /// </summary>
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

    private void OnBindOutput(WlClient client, uint version, uint id) =>
        _ = new ExtOutputImageCaptureSourceManagerV1Resource(client, version, id, this);

    private void OnBindToplevel(WlClient client, uint version, uint id) =>
        _ = new ExtForeignToplevelImageCaptureSourceManagerV1Resource(client, version, id, this);

    void IExtOutputImageCaptureSourceManagerV1Handler.CreateSource(ExtOutputImageCaptureSourceManagerV1Resource resource, in ExtOutputImageCaptureSourceManagerV1Resource.CreateSourceEventArgs args)
    {
        var source = new ExtImageCaptureSourceV1Resource(resource.Client, resource.Version, args.Source);
        var output = OutputGlobal.FromResource(args.Output)?.Output;
        Register(source, output is null ? default : CaptureSource.Output(output));
    }

    void IExtOutputImageCaptureSourceManagerV1Handler.Destroy(ExtOutputImageCaptureSourceManagerV1Resource resource)
    {
    }

    void IExtForeignToplevelImageCaptureSourceManagerV1Handler.CreateSource(ExtForeignToplevelImageCaptureSourceManagerV1Resource resource, in ExtForeignToplevelImageCaptureSourceManagerV1Resource.CreateSourceEventArgs args)
    {
        var source = new ExtImageCaptureSourceV1Resource(resource.Client, resource.Version, args.Source);
        var toplevelId = args.ToplevelHandle is { } handle ? ForeignToplevelListManager.ToplevelOf(handle.RawHandle) : 0;
        Register(source, toplevelId == 0 ? default : CaptureSource.Toplevel(toplevelId));
    }

    void IExtForeignToplevelImageCaptureSourceManagerV1Handler.Destroy(ExtForeignToplevelImageCaptureSourceManagerV1Resource resource)
    {
    }
}
