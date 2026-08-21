using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class PrimarySelectionManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly ISelectionStore? _store;
    private readonly Seat.Seat? _seat;
    private readonly List<ZwpPrimarySelectionDeviceV1Resource> _devices = [];

    public PrimarySelectionManager(WlServerDisplay display, ISelectionStore? store, Seat.Seat? seat)
    {
        ArgumentNullException.ThrowIfNull(display);
        _store = store;
        _seat = seat;
        _global = display.CreateGlobal(ZwpPrimarySelectionDeviceManagerV1.Interface, Version, OnBind);
        if (_store is { } live)
        {
            live.SelectionChanged += OnChanged;
        }

        if (_seat is { } withSeat)
        {
            withSeat.Keyboard.FocusChanged += OnFocusChanged;
        }
    }

    public void Dispose()
    {
        if (_store is { } live)
        {
            live.SelectionChanged -= OnChanged;
        }

        if (_seat is { } withSeat)
        {
            withSeat.Keyboard.FocusChanged -= OnFocusChanged;
        }

        _global.Dispose();
    }

    private void OnFocusChanged(Surface? surface) => OfferToFocus();

    private void OnChanged(SelectionKind kind)
    {
        if (kind == SelectionKind.Primary)
        {
            OfferToFocus();
        }
    }

    private void OfferToFocus()
    {
        if (_seat?.Keyboard.Focus is not { IsDestroyed: false } focus)
        {
            return;
        }

        var client = focus.Resource.Client;
        foreach (var device in _devices)
        {
            if (!device.IsDestroyed && device.Client == client)
            {
                SendSelection(device);
            }
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpPrimarySelectionDeviceManagerV1Resource(client, version, id);
        manager.CreateSource += (_, e) =>
        {
            var resource = new ZwpPrimarySelectionSourceV1Resource(client, manager.Version, e.Id);
            _ = new PrimarySource(resource);
        };
        manager.GetDevice += (_, e) =>
        {
            var device = new ZwpPrimarySelectionDeviceV1Resource(client, manager.Version, e.Id);
            _devices.Add(device);
            device.Destroyed += (_, _) => _devices.Remove(device);
            device.SetSelection += (_, se) =>
                _store?.SetSelection(SelectionKind.Primary, PrimarySource.Resolve(se.Source)?.Wrapped, se.Serial);

            if (_seat?.Keyboard.Focus is { IsDestroyed: false } focus && focus.Resource.Client == client)
            {
                SendSelection(device);
            }
        };
    }

    private void SendSelection(ZwpPrimarySelectionDeviceV1Resource device)
    {
        var source = _store?.Current(SelectionKind.Primary);
        ZwpPrimarySelectionOfferV1Resource? offer = null;
        if (source is not null)
        {
            offer = new ZwpPrimarySelectionOfferV1Resource(device.Client, device.Version, 0);
            device.SendDataOffer(offer);
            foreach (var mime in source.MimeTypes)
            {
                offer.SendOffer(mime);
            }

            offer.Receive += (_, e) => _store!.Receive(SelectionKind.Primary, e.MimeType, new ClientFd(e.Fd, offer.Client));
        }

        device.SendSelection(offer);
    }

    private sealed class PrimarySource
    {
        private static readonly Dictionary<ZwpPrimarySelectionSourceV1Resource, PrimarySource> Registry = [];
        private readonly List<string> _mimes = [];

        public PrimarySource(ZwpPrimarySelectionSourceV1Resource resource)
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

        public static PrimarySource? Resolve(ZwpPrimarySelectionSourceV1Resource? resource) =>
            resource is not null && Registry.TryGetValue(resource, out var source) ? source : null;
    }
}
