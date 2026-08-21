using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class ExtDataControlManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly ISelectionStore? _store;
    private readonly List<ExtDataControlDeviceV1Resource> _devices = [];

    public ExtDataControlManager(WlServerDisplay display, ISelectionStore? store)
    {
        ArgumentNullException.ThrowIfNull(display);
        _store = store;
        _global = display.CreateGlobal(ExtDataControlManagerV1.Interface, Version, OnBind);
        if (_store is { } live)
        {
            live.SelectionChanged += OnSelectionChanged;
        }
    }

    public void Dispose()
    {
        if (_store is { } live)
        {
            live.SelectionChanged -= OnSelectionChanged;
        }

        _global.Dispose();
    }

    private void OnSelectionChanged(SelectionKind kind) => Broadcast(kind == SelectionKind.Primary);

    private void Broadcast(bool primary)
    {
        foreach (var device in _devices)
        {
            if (!device.IsDestroyed)
            {
                SendSelection(device, primary);
            }
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ExtDataControlManagerV1Resource(client, version, id);
        manager.CreateDataSource += (_, e) =>
        {
            var resource = new ExtDataControlSourceV1Resource(client, manager.Version, e.Id);
            _ = new ControlSource(resource);
        };
        manager.GetDataDevice += (_, e) =>
        {
            var device = new ExtDataControlDeviceV1Resource(client, manager.Version, e.Id);
            _devices.Add(device);
            device.Destroyed += (_, _) => _devices.Remove(device);

            device.SetSelection += (_, se) =>
                _store?.SetSelection(SelectionKind.Clipboard, ControlSource.Resolve(se.Source)?.Wrapped, SelectionSerial.Unchecked);
            device.SetPrimarySelection += (_, se) =>
                _store?.SetSelection(SelectionKind.Primary, ControlSource.Resolve(se.Source)?.Wrapped, SelectionSerial.Unchecked);

            SendSelection(device, primary: false);
            SendSelection(device, primary: true);
        };
    }

    private void SendSelection(ExtDataControlDeviceV1Resource device, bool primary)
    {
        var kind = primary ? SelectionKind.Primary : SelectionKind.Clipboard;
        var source = _store?.Current(kind);
        ExtDataControlOfferV1Resource? offer = null;
        if (source is not null)
        {
            offer = new ExtDataControlOfferV1Resource(device.Client, device.Version, 0);
            device.SendDataOffer(offer);
            foreach (var mime in source.MimeTypes)
            {
                offer.SendOffer(mime);
            }

            offer.Receive += (_, e) => _store!.Receive(kind, e.MimeType, new ClientFd(e.Fd, offer.Client));
        }

        if (primary)
        {
            device.SendPrimarySelection(offer);
        }
        else
        {
            device.SendSelection(offer);
        }
    }

    private sealed class ControlSource
    {
        private static readonly Dictionary<ExtDataControlSourceV1Resource, ControlSource> Registry = [];
        private readonly List<string> _mimes = [];

        public ControlSource(ExtDataControlSourceV1Resource resource)
        {
            resource.Offer += (_, e) => _mimes.Add(e.MimeType);
            Wrapped = new DataSource(
                _mimes,
                (mime, fd) =>
                {
                    if (!resource.IsDestroyed)
                    {
                        resource.SendSend(mime, fd.Value);
                    }

                    fd.Close();
                },
                () =>
                {
                    if (!resource.IsDestroyed)
                    {
                        resource.SendCancelled();
                    }
                },
                resource.Client);
            resource.Destroyed += (_, _) =>
            {
                Registry.Remove(resource);
                Wrapped.MarkDestroyed();
            };
            Registry[resource] = this;
        }

        public DataSource Wrapped { get; }

        public static ControlSource? Resolve(ExtDataControlSourceV1Resource? resource) =>
            resource is not null && Registry.TryGetValue(resource, out var source) ? source : null;
    }
}
