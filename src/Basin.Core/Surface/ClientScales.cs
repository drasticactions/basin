using Wayland.Server;

namespace Basin;

/// <summary>
/// The default server scale of each client's surfaces. A client with no entry has scale 1. An entry goes away when its client disconnects.
/// </summary>
public sealed class ClientScales
{
    private readonly Dictionary<WlClient, double> _scales = [];
    private readonly HashSet<WlClient> _watched = [];

    /// <summary>Occurs after <see cref="Set"/> changes a client's scale, with the client and the new scale.</summary>
    public event Action<WlClient, double>? Changed;

    /// <summary>Gets the server scale of a client.</summary>
    /// <param name="client">The client.</param>
    /// <returns>The scale that was set, or 1 when none was set.</returns>
    public double ScaleOf(WlClient client) => _scales.GetValueOrDefault(client, 1.0);

    /// <summary>
    /// Sets a client's server scale. A new surface of the client reads it at creation, and live surfaces without an override follow the change.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="scale">The factor by which the client's surface coordinates exceed logical coordinates. It must be positive and finite.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not positive and finite.</exception>
    public void Set(WlClient client, double scale)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale must be positive and finite.");
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
