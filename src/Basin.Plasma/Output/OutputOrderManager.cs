using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class OutputOrderManager : IDisposable
{
    public const int Version = 1;

    private readonly IOutputOrder _order;
    private readonly WlGlobal _global;
    private readonly List<KdeOutputOrderV1Resource> _resources = [];
    private IOutput[] _scratch = new IOutput[8];

    public OutputOrderManager(WlServerDisplay display, IOutputOrder order)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(order);
        _order = order;
        _global = display.CreateGlobal(KdeOutputOrderV1.Interface, Version, OnBind);
        order.Changed += Broadcast;
    }

    public void Dispose()
    {
        _order.Changed -= Broadcast;
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new KdeOutputOrderV1Resource(client, version, id);
        _resources.Add(resource);
        resource.Destroyed += (_, _) => _resources.Remove(resource);
        Send(resource);
    }

    private void Broadcast()
    {
        foreach (var resource in _resources)
        {
            if (!resource.IsDestroyed)
            {
                Send(resource);
            }
        }
    }

    private void Send(KdeOutputOrderV1Resource resource)
    {
        foreach (var output in Ordered())
        {
            resource.SendOutput(output.Name);
        }

        resource.SendDone();
    }

    private ReadOnlySpan<IOutput> Ordered()
    {
        var count = _order.Enumerate(_scratch);
        while (count < 0)
        {
            _scratch = new IOutput[_scratch.Length * 2];
            count = _order.Enumerate(_scratch);
        }

        return _scratch.AsSpan(0, count);
    }
}
