using Basin;
using Basin.Diagnostics;
using Wayland;

namespace Basin.Portal.Client;

public sealed class ClientOutputs : IDisposable
{
    private readonly List<ClientOutput> _outputs = [];
    private readonly ClientGlobals _globals;
    private static readonly BasinLogger Log = BasinLog.For("portal-client");

    public ClientOutputs(ClientGlobals globals)
    {
        _globals = globals;
    }

    public OutputLayout Layout { get; } = new();

    public IReadOnlyList<ClientOutput> All => _outputs;

    public event Action? Changed;

    public void OnGlobal(WlRegistry registry, uint name, uint version)
    {
        var proxy = registry.Bind<WlOutput>(name, Math.Min(version, 4));
        var output = new ClientOutput(name, proxy);
        _outputs.Add(output);
        output.Ready += () => OnReady(output);
        output.Committed += _ => OnMoved(output);
        if (_globals.XdgOutputs is { } manager)
        {
            output.AttachXdgOutput(manager);
        }
    }

    public void AttachXdgOutputs()
    {
        if (_globals.XdgOutputs is not { } manager)
        {
            return;
        }

        foreach (var output in _outputs)
        {
            output.AttachXdgOutput(manager);
        }
    }

    public void OnGlobalRemoved(uint name)
    {
        for (var i = 0; i < _outputs.Count; i++)
        {
            var output = _outputs[i];
            if (output.RegistryName != name)
            {
                continue;
            }

            _outputs.RemoveAt(i);
            Log.Info($"output {output.Name} left");
            output.NotifyRemoved();
            Changed?.Invoke();
            return;
        }
    }

    public ClientOutput? ByName(string name)
    {
        foreach (var output in _outputs)
        {
            if (output.Name == name)
            {
                return output;
            }
        }

        return null;
    }

    public ClientOutput? At(double x, double y) => Layout.OutputAt(x, y) as ClientOutput;

    public void Dispose()
    {
        foreach (var output in _outputs.ToArray())
        {
            output.NotifyRemoved();
        }

        _outputs.Clear();
    }

    private void OnReady(ClientOutput output)
    {
        Layout.Add(output, output.Position.X, output.Position.Y);
        Log.Info($"output {output.Name} {output.CurrentMode.Width}x{output.CurrentMode.Height}@{output.Scale:0.##} at {output.Position.X},{output.Position.Y}");
        Changed?.Invoke();
    }

    private void OnMoved(ClientOutput output)
    {
        if (Layout.Contains(output))
        {
            Layout.Move(output, output.Position.X, output.Position.Y);
        }

        Changed?.Invoke();
    }
}
