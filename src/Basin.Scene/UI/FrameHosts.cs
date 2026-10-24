using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// An ordered list of <see cref="IUIHost"/> instances that a <see cref="Frame"/> can draw its chrome with.
/// The frame picks the first host whose surface contract its <see cref="IFrameRenderer"/> accepts.
/// </summary>
public sealed class FrameHosts
{
    private readonly List<IUIHost> _hosts = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameHosts"/> class.
    /// The hosts keep their order, and a duplicate host is added once.
    /// </summary>
    /// <param name="hosts">The hosts in order of preference.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hosts"/> or one of its items is <see langword="null"/>.</exception>
    public FrameHosts(params IUIHost[] hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        foreach (var host in hosts)
        {
            Add(host);
        }
    }

    /// <summary>
    /// Gets the hosts in order of preference. The list is live and shows later calls to <see cref="Add(IUIHost)"/> and <see cref="Remove(IUIHost)"/>.
    /// </summary>
    public IReadOnlyList<IUIHost> Hosts => _hosts;

    /// <summary>
    /// Appends a host at the lowest preference. A host that is already in the list stays where it is.
    /// </summary>
    /// <param name="host">The host to append.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public void Add(IUIHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!_hosts.Contains(host))
        {
            _hosts.Add(host);
        }
    }

    /// <summary>
    /// Removes a host from the list. A frame that already has a surface from this host keeps it.
    /// </summary>
    /// <param name="host">The host to remove. A host that is not in the list is ignored.</param>
    public void Remove(IUIHost host) => _hosts.Remove(host);

    /// <summary>
    /// Finds the first host whose surface contract is assignable to the contract of <paramref name="renderer"/>.
    /// </summary>
    /// <param name="renderer">The renderer that will draw into the surface.</param>
    /// <returns>The matching host, or <see langword="null"/> when no host can serve the renderer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public IUIHost? For(IFrameRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        var wanted = renderer.SurfaceContract;
        for (var i = 0; i < _hosts.Count; i++)
        {
            if (wanted.IsAssignableFrom(_hosts[i].SurfaceContract))
            {
                return _hosts[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Makes a diagnostic text that names the surface contract of a renderer and the contracts that the hosts offer.
    /// </summary>
    /// <param name="renderer">The renderer to describe.</param>
    /// <returns>One line of text for an error message or a log.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public string Describe(IFrameRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        var offered = _hosts.Count == 0
            ? "none"
            : string.Join(", ", _hosts.Select(host => host.SurfaceContract.Name));
        return $"{renderer.GetType().Name} draws into {renderer.SurfaceContract.Name}; hosts offer {offered}";
    }
}
