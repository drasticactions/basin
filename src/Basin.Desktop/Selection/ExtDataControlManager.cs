using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>ext_data_control_manager_v1</c> global. Clipboard managers read and set the clipboard and primary selection through <see cref="ISelectionStore"/>, without focus.
/// </summary>
public sealed class ExtDataControlManager : IDisposable, IExtDataControlManagerV1Handler, IExtDataControlDeviceV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly ISelectionStore? _store;
    private readonly List<ExtDataControlDeviceV1Resource> _devices = [];
    private readonly OfferReceiver _clipboardOffers;
    private readonly OfferReceiver _primaryOffers;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtDataControlManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="store">The selection store, or <see langword="null"/> to offer no selection and ignore every set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public ExtDataControlManager(WlServerDisplay display, ISelectionStore? store)
    {
        ArgumentNullException.ThrowIfNull(display);
        _store = store;
        _clipboardOffers = new OfferReceiver(store, SelectionKind.Clipboard);
        _primaryOffers = new OfferReceiver(store, SelectionKind.Primary);
        _global = display.CreateGlobal(ExtDataControlManagerV1.Interface, Version, OnBind);
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
        foreach (var device in _devices)
        {
            if (!device.IsDestroyed)
            {
                SendSelection(device, primary);
            }
        }
    }

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new ExtDataControlManagerV1Resource(client, version, id, this);

    void IExtDataControlManagerV1Handler.CreateDataSource(ExtDataControlManagerV1Resource resource, in ExtDataControlManagerV1Resource.CreateDataSourceEventArgs args) =>
        _ = new ControlSource(resource.Client, resource.Version, args.Id);

    void IExtDataControlManagerV1Handler.GetDataDevice(ExtDataControlManagerV1Resource resource, in ExtDataControlManagerV1Resource.GetDataDeviceEventArgs args)
    {
        var device = new ExtDataControlDeviceV1Resource(resource.Client, resource.Version, args.Id, this);
        _devices.Add(device);
        device.Destroyed += (_, _) => _devices.Remove(device);

        SendSelection(device, primary: false);
        SendSelection(device, primary: true);
    }

    void IExtDataControlManagerV1Handler.Destroy(ExtDataControlManagerV1Resource resource)
    {
    }

    void IExtDataControlDeviceV1Handler.SetSelection(ExtDataControlDeviceV1Resource resource, in ExtDataControlDeviceV1Resource.SetSelectionEventArgs args) =>
        _store?.SetSelection(SelectionKind.Clipboard, ControlSource.Resolve(args.Source)?.Wrapped, SelectionSerial.Unchecked);

    void IExtDataControlDeviceV1Handler.SetPrimarySelection(ExtDataControlDeviceV1Resource resource, in ExtDataControlDeviceV1Resource.SetPrimarySelectionEventArgs args) =>
        _store?.SetSelection(SelectionKind.Primary, ControlSource.Resolve(args.Source)?.Wrapped, SelectionSerial.Unchecked);

    void IExtDataControlDeviceV1Handler.Destroy(ExtDataControlDeviceV1Resource resource)
    {
    }

    private void SendSelection(ExtDataControlDeviceV1Resource device, bool primary)
    {
        var kind = primary ? SelectionKind.Primary : SelectionKind.Clipboard;
        var source = _store?.Current(kind);
        ExtDataControlOfferV1Resource? offer = null;
        if (source is not null)
        {
            offer = new ExtDataControlOfferV1Resource(device.Client, device.Version, 0, primary ? _primaryOffers : _clipboardOffers);
            device.SendDataOffer(offer);
            foreach (var mime in source.MimeTypes)
            {
                offer.SendOffer(mime);
            }
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

    private sealed class OfferReceiver(ISelectionStore? store, SelectionKind kind) : IExtDataControlOfferV1Handler
    {
        void IExtDataControlOfferV1Handler.Receive(ExtDataControlOfferV1Resource resource, in ExtDataControlOfferV1Resource.ReceiveEventArgs args) =>
            store!.Receive(kind, args.MimeType, new ClientFd(args.Fd, resource.Client));

        void IExtDataControlOfferV1Handler.Destroy(ExtDataControlOfferV1Resource resource)
        {
        }
    }

    private sealed class ControlSource : IExtDataControlSourceV1Handler
    {
        private static readonly Dictionary<ExtDataControlSourceV1Resource, ControlSource> Registry = [];
        private readonly List<string> _mimes = [];

        public ControlSource(WlClient client, uint version, uint id)
        {
            var resource = new ExtDataControlSourceV1Resource(client, version, id, this);
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

        void IExtDataControlSourceV1Handler.Offer(ExtDataControlSourceV1Resource resource, in ExtDataControlSourceV1Resource.OfferEventArgs args) =>
            _mimes.Add(args.MimeType);

        void IExtDataControlSourceV1Handler.Destroy(ExtDataControlSourceV1Resource resource)
        {
        }
    }
}
