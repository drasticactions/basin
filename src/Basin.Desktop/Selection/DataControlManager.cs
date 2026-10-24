using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwlr_data_control_manager_v1</c> global. Clipboard managers read and set the clipboard and primary selection through <see cref="ISelectionStore"/>, without focus.
/// </summary>
public sealed class DataControlManager : IDisposable
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly ISelectionStore? _store;
    private readonly List<DeviceEntry> _devices = [];

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

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwlrDataControlManagerV1Resource(client, version, id);
        manager.CreateDataSource += (_, e) =>
        {
            var resource = new ZwlrDataControlSourceV1Resource(client, manager.Version, e.Id);
            _ = new ControlSource(resource);
        };
        manager.GetDataDevice += (_, e) =>
        {
            var device = new ZwlrDataControlDeviceV1Resource(client, manager.Version, e.Id);
            var entry = new DeviceEntry { Resource = device };
            _devices.Add(entry);
            device.Destroyed += (_, _) => _devices.Remove(entry);

            device.SetSelection += (_, se) =>
                _store?.SetSelection(SelectionKind.Clipboard, ControlSource.Resolve(se.Source)?.Wrapped, SelectionSerial.Unchecked);
            device.SetPrimarySelection += (_, se) =>
                _store?.SetSelection(SelectionKind.Primary, ControlSource.Resolve(se.Source)?.Wrapped, SelectionSerial.Unchecked);

            SendSelection(device, primary: false);
            if (device.Version >= 2)
            {
                SendSelection(device, primary: true);
            }
        };
    }

    private void SendSelection(ZwlrDataControlDeviceV1Resource device, bool primary)
    {
        var kind = primary ? SelectionKind.Primary : SelectionKind.Clipboard;
        var source = _store?.Current(kind);
        ZwlrDataControlOfferV1Resource? offer = null;
        if (source is not null)
        {
            offer = new ZwlrDataControlOfferV1Resource(device.Client, device.Version, 0);
            device.SendDataOffer(offer);
            foreach (var mime in source.MimeTypes)
            {
                offer.SendOffer(mime);
            }

            offer.Receive += (_, e) => _store!.Receive(kind, e.MimeType, new ClientFd(e.Fd, offer.Client));
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

    private sealed class ControlSource
    {
        private static readonly Dictionary<ZwlrDataControlSourceV1Resource, ControlSource> Registry = [];
        private readonly List<string> _mimes = [];

        public ControlSource(ZwlrDataControlSourceV1Resource resource)
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

        public static ControlSource? Resolve(ZwlrDataControlSourceV1Resource? resource) =>
            resource is not null && Registry.TryGetValue(resource, out var source) ? source : null;
    }
}
