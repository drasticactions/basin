using Wayland.Server;

namespace Basin;

public sealed class ClientScales
{
    private readonly Dictionary<WlClient, double> _scales = [];
    private readonly HashSet<WlClient> _watched = [];

    public event Action<WlClient, double>? Changed;

    public double ScaleOf(WlClient client) => _scales.GetValueOrDefault(client, 1.0);

    public void Set(WlClient client, double scale)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "a server scale must be positive and finite");
        }

        var previous = ScaleOf(client);
        if (previous == scale)
        {
            return;
        }

        if (scale == 1.0)
        {
            _scales.Remove(client);
        }
        else
        {
            if (_watched.Add(client))
            {
                client.Destroyed += () =>
                {
                    _scales.Remove(client);
                    _watched.Remove(client);
                };
            }

            _scales[client] = scale;
        }

        Changed?.Invoke(client, scale);
    }
}
