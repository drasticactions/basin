using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwlr_data_control_manager_v1</c> global. Clipboard managers read and set the clipboard and primary selection through <see cref="ISelectionStore"/>, without focus.
/// </summary>
public sealed class DataControlManager : IDisposable, IZwlrDataControlManagerV1Handler, IZwlrDataControlDeviceV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly ISelectionStore? _store;
    private readonly List<DeviceEntry> _devices = [];
    private readonly OfferReceiver _clipboardOffers;
    private readonly OfferReceiver _primaryOffers;

    private sealed class DeviceEntry
    {
        public required ZwlrDataControlDeviceV1Resource Resource;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DataControlManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="store">The selection store, or <see langword="null"/> to offer no selection and ignore every set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public DataControlManager(WlServerDisplay display, ISelectionStore? store)
    {
        ArgumentNullException.ThrowIfNull(display);
        _store = store;
        _clipboardOffers = new OfferReceiver(store, SelectionKind.Clipboard);
        _primaryOffers = new OfferReceiver(store, SelectionKind.Primary);
        _global = display.CreateGlobal(ZwlrDataControlManagerV1.Interface, Version, OnBind);
        if (_store is { } live)
        {
            live.SelectionChanged += OnSelectionChanged;
        }
    }

    /// <summary>
    /// Stops following the store and removes the global.
    /// </summary>
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
        foreach (var entry in _devices)
        {
            if (!entry.Resource.IsDestroyed)
            {
                SendSelection(entry.Resource, primary);
            }
        }
    }

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new ZwlrDataControlManagerV1Resource(client, version, id, this);

    void IZwlrDataControlManagerV1Handler.CreateDataSource(ZwlrDataControlManagerV1Resource resource, in ZwlrDataControlManagerV1Resource.CreateDataSourceEventArgs args) =>
        _ = new ControlSource(resource.Client, resource.Version, args.Id);

    void IZwlrDataControlManagerV1Handler.GetDataDevice(ZwlrDataControlManagerV1Resource resource, in ZwlrDataControlManagerV1Resource.GetDataDeviceEventArgs args)
    {
        var device = new ZwlrDataControlDeviceV1Resource(resource.Client, resource.Version, args.Id, this);
        var entry = new DeviceEntry { Resource = device };
        _devices.Add(entry);
        device.Destroyed += (_, _) => _devices.Remove(entry);

        SendSelection(device, primary: false);
        if (device.Version >= 2)
        {
            SendSelection(device, primary: true);
        }
    }

    void IZwlrDataControlManagerV1Handler.Destroy(ZwlrDataControlManagerV1Resource resource)
    {
    }

    void IZwlrDataControlDeviceV1Handler.SetSelection(ZwlrDataControlDeviceV1Resource resource, in ZwlrDataControlDeviceV1Resource.SetSelectionEventArgs args) =>
        _store?.SetSelection(SelectionKind.Clipboard, ControlSource.Resolve(args.Source)?.Wrapped, SelectionSerial.Unchecked);

    void IZwlrDataControlDeviceV1Handler.SetPrimarySelection(ZwlrDataControlDeviceV1Resource resource, in ZwlrDataControlDeviceV1Resource.SetPrimarySelectionEventArgs args) =>
        _store?.SetSelection(SelectionKind.Primary, ControlSource.Resolve(args.Source)?.Wrapped, SelectionSerial.Unchecked);

    void IZwlrDataControlDeviceV1Handler.Destroy(ZwlrDataControlDeviceV1Resource resource)
    {
    }

    private void SendSelection(ZwlrDataControlDeviceV1Resource device, bool primary)
    {
        var kind = primary ? SelectionKind.Primary : SelectionKind.Clipboard;
        var source = _store?.Current(kind);
        ZwlrDataControlOfferV1Resource? offer = null;
        if (source is not null)
        {
            offer = new ZwlrDataControlOfferV1Resource(device.Client, device.Version, 0, primary ? _primaryOffers : _clipboardOffers);
            device.SendDataOffer(offer);
            foreach (var mime in source.MimeTypes)
            {
                offer.SendOffer(mime);
            }
        }

        if (primary)
        {
            if (device.Version >= 2)
            {
                device.SendPrimarySelection(offer);
            }
        }
        else
        {
            device.SendSelection(offer);
        }
    }

    private sealed class OfferReceiver(ISelectionStore? store, SelectionKind kind) : IZwlrDataControlOfferV1Handler
    {
        void IZwlrDataControlOfferV1Handler.Receive(ZwlrDataControlOfferV1Resource resource, in ZwlrDataControlOfferV1Resource.ReceiveEventArgs args) =>
            store!.Receive(kind, args.MimeType, new ClientFd(args.Fd, resource.Client));

        void IZwlrDataControlOfferV1Handler.Destroy(ZwlrDataControlOfferV1Resource resource)
        {
        }
    }

    private sealed class ControlSource : IZwlrDataControlSourceV1Handler
    {
        private static readonly Dictionary<ZwlrDataControlSourceV1Resource, ControlSource> Registry = [];
        private readonly List<string> _mimes = [];

        public ControlSource(WlClient client, uint version, uint id)
        {
            var resource = new ZwlrDataControlSourceV1Resource(client, version, id, this);
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

        public static ControlSource? Resolve(ZwlrDataControlSourceV1Resource? resource) =>
            resource is not null && Registry.TryGetValue(resource, out var source) ? source : null;

        void IZwlrDataControlSourceV1Handler.Offer(ZwlrDataControlSourceV1Resource resource, in ZwlrDataControlSourceV1Resource.OfferEventArgs args) =>
            _mimes.Add(args.MimeType);

        void IZwlrDataControlSourceV1Handler.Destroy(ZwlrDataControlSourceV1Resource resource)
        {
        }
    }
}
