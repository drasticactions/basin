using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the xdg-foreign exporter and importer globals, v1 and v2. A client can make a surface of another client the parent of its own surface.
/// </summary>
public sealed class XdgForeignManager : IDisposable, IZxdgExporterV1Handler, IZxdgImporterV1Handler, IZxdgExporterV2Handler, IZxdgImporterV2Handler
{
    /// <summary>
    /// The protocol version that all four globals advertise.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _exporterV1Global;
    private readonly WlGlobal _importerV1Global;
    private readonly WlGlobal _exporterGlobal;
    private readonly WlGlobal _importerGlobal;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<string, Exported> _exports = [];
    private int _handleCounter;

    private interface IImported
    {
        bool IsDestroyed { get; }

        void SendDestroyed();
    }

    private sealed class Exported
    {
        public required Surface Surface;
        public required List<IImported> Importers;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgForeignManager"/> class and creates the four globals.
    /// </summary>
    /// <param name="display">The display that holds the globals.</param>
    /// <param name="compositor">The compositor global that resolves surfaces.</param>
    public XdgForeignManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _exporterV1Global = display.CreateGlobal(ZxdgExporterV1.Interface, Version, OnBindExporterV1);
        _importerV1Global = display.CreateGlobal(ZxdgImporterV1.Interface, Version, OnBindImporterV1);
        _exporterGlobal = display.CreateGlobal(ZxdgExporterV2.Interface, Version, OnBindExporter);
        _importerGlobal = display.CreateGlobal(ZxdgImporterV2.Interface, Version, OnBindImporter);
    }

    /// <summary>
    /// Occurs when a client sets an imported surface as the parent of its own surface. The arguments are the child and the parent.
    /// </summary>
    public event Action<Surface, Surface>? ParentRequested;

    /// <summary>
    /// Removes the four globals. Bound clients keep their resources.
    /// </summary>
    public void Dispose()
    {
        _exporterV1Global.Dispose();
        _importerV1Global.Dispose();
        _exporterGlobal.Dispose();
        _importerGlobal.Dispose();
    }

    private Action Export(Surface surface, Action<string> sendHandle)
    {
        var handle = $"basin-export-{++_handleCounter}";
        _exports[handle] = new Exported { Surface = surface, Importers = [] };
        sendHandle(handle);

        return () =>
        {
            if (_exports.Remove(handle, out var removed))
            {
                foreach (var importer in removed.Importers)
                {
                    if (!importer.IsDestroyed)
                    {
                        importer.SendDestroyed();
                    }
                }
            }
        };
    }

    private Exported? Import(string handle, IImported imported)
    {
        if (!_exports.TryGetValue(handle, out var entry))
        {
            imported.SendDestroyed();
            return null;
        }

        entry.Importers.Add(imported);
        return entry;
    }

    private void SetParentOf(Exported entry, WlSurfaceResource? childResource)
    {
        if (_compositor.ResolveSurface(childResource) is { } child)
        {
            ParentRequested?.Invoke(child, entry.Surface);
        }
    }

    private void OnBindExporterV1(WlClient client, uint version, uint id) =>
        _ = new ZxdgExporterV1Resource(client, version, id, this);

    private void OnBindImporterV1(WlClient client, uint version, uint id) =>
        _ = new ZxdgImporterV1Resource(client, version, id, this);

    private void OnBindExporter(WlClient client, uint version, uint id) =>
        _ = new ZxdgExporterV2Resource(client, version, id, this);

    private void OnBindImporter(WlClient client, uint version, uint id) =>
        _ = new ZxdgImporterV2Resource(client, version, id, this);

    void IZxdgExporterV1Handler.Export(ZxdgExporterV1Resource resource, in ZxdgExporterV1Resource.ExportEventArgs args)
    {
        var exported = new ZxdgExportedV1Resource(resource.Client, resource.Version, args.Id);
        if (_compositor.ResolveSurface(args.Surface) is not { } surface)
        {
            return;
        }

        var drop = Export(surface, exported.SendHandle);
        exported.Destroyed += (_, _) => drop();
        surface.Destroyed += drop;
    }

    void IZxdgExporterV1Handler.Destroy(ZxdgExporterV1Resource resource)
    {
    }

    void IZxdgImporterV1Handler.Import(ZxdgImporterV1Resource resource, in ZxdgImporterV1Resource.ImportEventArgs args)
    {
        var imported = new ImportedV1(this, resource.Client, resource.Version, args.Id);
        if (Import(args.Handle, imported) is not { } entry)
        {
            return;
        }

        imported.Entry = entry;
        imported.Resource.Destroyed += (_, _) => entry.Importers.Remove(imported);
    }

    void IZxdgImporterV1Handler.Destroy(ZxdgImporterV1Resource resource)
    {
    }

    void IZxdgExporterV2Handler.ExportToplevel(ZxdgExporterV2Resource resource, in ZxdgExporterV2Resource.ExportToplevelEventArgs args)
    {
        var exported = new ZxdgExportedV2Resource(resource.Client, resource.Version, args.Id);
        if (_compositor.ResolveSurface(args.Surface) is not { } surface)
        {
            return;
        }

        var drop = Export(surface, exported.SendHandle);
        exported.Destroyed += (_, _) => drop();
        surface.Destroyed += drop;
    }

    void IZxdgExporterV2Handler.Destroy(ZxdgExporterV2Resource resource)
    {
    }

    void IZxdgImporterV2Handler.ImportToplevel(ZxdgImporterV2Resource resource, in ZxdgImporterV2Resource.ImportToplevelEventArgs args)
    {
        var imported = new ImportedV2(this, resource.Client, resource.Version, args.Id);
        if (Import(args.Handle, imported) is not { } entry)
        {
            return;
        }

        imported.Entry = entry;
        imported.Resource.Destroyed += (_, _) => entry.Importers.Remove(imported);
    }

    void IZxdgImporterV2Handler.Destroy(ZxdgImporterV2Resource resource)
    {
    }

    private sealed class ImportedV1 : IImported, IZxdgImportedV1Handler
    {
        private readonly XdgForeignManager _owner;

        public ImportedV1(XdgForeignManager owner, WlClient client, uint version, uint id)
        {
            _owner = owner;
            Resource = new ZxdgImportedV1Resource(client, version, id, this);
        }

        public ZxdgImportedV1Resource Resource { get; }

        public Exported? Entry { get; set; }

        public bool IsDestroyed => Resource.IsDestroyed;

        public void SendDestroyed() => Resource.SendDestroyed();

        void IZxdgImportedV1Handler.SetParentOf(ZxdgImportedV1Resource resource, in ZxdgImportedV1Resource.SetParentOfEventArgs args)
        {
            if (Entry is { } entry)
            {
                _owner.SetParentOf(entry, args.Surface);
            }
        }

        void IZxdgImportedV1Handler.Destroy(ZxdgImportedV1Resource resource)
        {
        }
    }

    private sealed class ImportedV2 : IImported, IZxdgImportedV2Handler
    {
        private readonly XdgForeignManager _owner;

        public ImportedV2(XdgForeignManager owner, WlClient client, uint version, uint id)
        {
            _owner = owner;
            Resource = new ZxdgImportedV2Resource(client, version, id, this);
        }

        public ZxdgImportedV2Resource Resource { get; }

        public Exported? Entry { get; set; }

        public bool IsDestroyed => Resource.IsDestroyed;

        public void SendDestroyed() => Resource.SendDestroyed();

        void IZxdgImportedV2Handler.SetParentOf(ZxdgImportedV2Resource resource, in ZxdgImportedV2Resource.SetParentOfEventArgs args)
        {
            if (Entry is { } entry)
            {
                _owner.SetParentOf(entry, args.Surface);
            }
        }

        void IZxdgImportedV2Handler.Destroy(ZxdgImportedV2Resource resource)
        {
        }
    }
}
