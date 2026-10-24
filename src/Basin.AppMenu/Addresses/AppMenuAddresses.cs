namespace Basin.AppMenu;

/// <summary>
/// The table of menu addresses that clients report, one entry for each <see cref="Surface"/>.
/// The appmenu protocol modules write it, and the compositor reads it. It must be used on the compositor thread.
/// </summary>
public sealed class AppMenuAddresses
{
    private readonly Dictionary<Surface, Entry> _addresses = [];

    /// <summary>
    /// Gets or sets a value indicating whether the compositor shows a global menu bar. GTK exports a menu bar only when it is <see langword="true"/>.
    /// After a change, call <see cref="GtkShellManager.RefreshCapabilities"/> to tell bound clients.
    /// </summary>
    public bool ShowsMenuBar { get; set; }

    /// <summary>
    /// Occurs when the address of a surface changes. The arguments are the surface and the new address, which is <see langword="null"/> when the entry is removed.
    /// It also fires with <see langword="null"/> when a surface with an entry is destroyed.
    /// </summary>
    public event Action<Surface, AppMenuAddress?>? Changed;

    /// <summary>
    /// Gets the number of surfaces that have an address.
    /// </summary>
    public int Count => _addresses.Count;

    /// <summary>
    /// Finds the menu address of a surface.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The address, or <see langword="null"/> when the surface has none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public AppMenuAddress? AddressOf(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return _addresses.GetValueOrDefault(surface)?.Address;
    }

    /// <summary>
    /// Records or removes the menu address of a surface, and raises <see cref="Changed"/> when the entry changes.
    /// The entry is removed by itself when the surface is destroyed.
    /// </summary>
    /// <param name="surface">The surface that the address belongs to.</param>
    /// <param name="address">The new address, or <see langword="null"/> to remove the entry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public void Set(Surface surface, AppMenuAddress? address)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (address is null)
        {
            if (_addresses.Remove(surface, out var removed))
            {
                surface.Destroyed -= removed.OnDestroyed;
                Changed?.Invoke(surface, null);
            }

            return;
        }

        if (_addresses.TryGetValue(surface, out var entry))
        {
            if (entry.Address == address)
            {
                return;
            }

            entry.Address = address;
        }
        else
        {
            entry = new Entry(address, () => Set(surface, null));
            surface.Destroyed += entry.OnDestroyed;
            _addresses[surface] = entry;
        }

        Changed?.Invoke(surface, address);
    }

    private sealed class Entry(AppMenuAddress address, Action onDestroyed)
    {
        public AppMenuAddress Address { get; set; } = address;

        public Action OnDestroyed { get; } = onDestroyed;
    }
}
