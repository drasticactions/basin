using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class XdgForeignManager : IDisposable
{
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

    public XdgForeignManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _exporterV1Global = display.CreateGlobal(ZxdgExporterV1.Interface, Version, OnBindExporterV1);
        _importerV1Global = display.CreateGlobal(ZxdgImporterV1.Interface, Version, OnBindImporterV1);
        _exporterGlobal = display.CreateGlobal(ZxdgExporterV2.Interface, Version, OnBindExporter);
        _importerGlobal = display.CreateGlobal(ZxdgImporterV2.Interface, Version, OnBindImporter);
    }

    public event Action<Surface, Surface>? ParentRequested;

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

    private void OnBindExporterV1(WlClient client, uint version, uint id)
    {
        var exporter = new ZxdgExporterV1Resource(client, version, id);
        exporter.Export += (_, e) =>
        {
            var exported = new ZxdgExportedV1Resource(client, exporter.Version, e.Id);
            if (_compositor.ResolveSurface(e.Surface) is not { } surface)
            {
                return;
            }

            var drop = Export(surface, exported.SendHandle);
            exported.Destroyed += (_, _) => drop();
            surface.Destroyed += drop;
        };
    }

    private void OnBindImporterV1(WlClient client, uint version, uint id)
    {
        var importer = new ZxdgImporterV1Resource(client, version, id);
        importer.Import += (_, e) =>
        {
            var resource = new ZxdgImportedV1Resource(client, importer.Version, e.Id);
            var imported = new ImportedV1(resource);
            if (Import(e.Handle, imported) is not { } entry)
            {
                return;
            }

            resource.Destroyed += (_, _) => entry.Importers.Remove(imported);
            resource.SetParentOf += (_, pe) => SetParentOf(entry, pe.Surface);
        };
    }

    private void OnBindExporter(WlClient client, uint version, uint id)
    {
        var exporter = new ZxdgExporterV2Resource(client, version, id);
        exporter.ExportToplevel += (_, e) =>
        {
            var exported = new ZxdgExportedV2Resource(client, exporter.Version, e.Id);
            if (_compositor.ResolveSurface(e.Surface) is not { } surface)
            {
                return;
            }

            var drop = Export(surface, exported.SendHandle);
            exported.Destroyed += (_, _) => drop();
            surface.Destroyed += drop;
        };
    }

    private void OnBindImporter(WlClient client, uint version, uint id)
    {
        var importer = new ZxdgImporterV2Resource(client, version, id);
        importer.ImportToplevel += (_, e) =>
        {
            var resource = new ZxdgImportedV2Resource(client, importer.Version, e.Id);
            var imported = new ImportedV2(resource);
            if (Import(e.Handle, imported) is not { } entry)
            {
                return;
            }

            resource.Destroyed += (_, _) => entry.Importers.Remove(imported);
            resource.SetParentOf += (_, pe) => SetParentOf(entry, pe.Surface);
        };
    }

    private sealed class ImportedV1 : IImported
    {
        private readonly ZxdgImportedV1Resource _resource;

        public ImportedV1(ZxdgImportedV1Resource resource) => _resource = resource;

        public bool IsDestroyed => _resource.IsDestroyed;

        public void SendDestroyed() => _resource.SendDestroyed();
    }

    private sealed class ImportedV2 : IImported
    {
        private readonly ZxdgImportedV2Resource _resource;

        public ImportedV2(ZxdgImportedV2Resource resource) => _resource = resource;

        public bool IsDestroyed => _resource.IsDestroyed;

        public void SendDestroyed() => _resource.SendDestroyed();
    }
}
