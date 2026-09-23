using Basin.Capabilities;

namespace Basin.Scene;

public sealed class FrameHosts
{
    private readonly List<IUIHost> _hosts = [];

    public FrameHosts(params IUIHost[] hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        foreach (var host in hosts)
        {
            Add(host);
        }
    }

    public IReadOnlyList<IUIHost> Hosts => _hosts;

    public void Add(IUIHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!_hosts.Contains(host))
        {
            _hosts.Add(host);
        }
    }

    public void Remove(IUIHost host) => _hosts.Remove(host);

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

    public string Describe(IFrameRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        var offered = _hosts.Count == 0
            ? "none"
            : string.Join(", ", _hosts.Select(host => host.SurfaceContract.Name));
        return $"{renderer.GetType().Name} draws into {renderer.SurfaceContract.Name}; hosts offer {offered}";
    }
}
