using Basin;
using Basin.Diagnostics;
using Wayland;

namespace Basin.Portal.Client;

/// <summary>
/// Tracks the outputs of the compositor that a portal client connects to, and keeps them in an <see cref="OutputLayout"/>.
/// </summary>
public sealed class ClientOutputs : IDisposable
{
    private readonly List<ClientOutput> _outputs = [];
    private readonly ClientGlobals _globals;
    private static readonly BasinLogger Log = BasinLog.For("portal-client");

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientOutputs"/> class.
    /// </summary>
    /// <param name="globals">The globals that supply the xdg-output manager when it is bound.</param>
    public ClientOutputs(ClientGlobals globals)
    {
        _globals = globals;
    }

    /// <summary>
    /// Gets the layout that holds each ready output at its announced position.
    /// </summary>
    public OutputLayout Layout { get; } = new();

    /// <summary>
    /// Gets every bound output, including outputs that are not ready yet.
    /// </summary>
    public IReadOnlyList<ClientOutput> All => _outputs;

    /// <summary>
    /// Occurs when an output becomes ready, changes its state or goes away.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Binds a new <c>wl_output</c> global at version 4 or lower and starts to track it.
    /// The output joins <see cref="Layout"/> when its first state is complete.
    /// </summary>
    /// <param name="registry">The registry that announced the global.</param>
    /// <param name="name">The numeric name of the global.</param>
    /// <param name="version">The version that the compositor offers.</param>
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

    /// <summary>
    /// Attaches xdg-output objects to the outputs that were bound before the xdg-output manager. It does nothing without the manager.
    /// </summary>
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

    /// <summary>
    /// Removes the output with this registry name, destroys it and raises <see cref="Changed"/>. An unknown name is ignored.
    /// </summary>
    /// <param name="name">The numeric name from the <c>global_remove</c> event.</param>
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

    /// <summary>
    /// Finds a bound output by its announced name.
    /// </summary>
    /// <param name="name">The output name, for example <c>DP-1</c>.</param>
    /// <returns>The output, or <see langword="null"/> when no output has the name.</returns>
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

    /// <summary>
    /// Finds the ready output that contains a point of the layout.
    /// </summary>
    /// <param name="x">The horizontal layout coordinate.</param>
    /// <param name="y">The vertical layout coordinate.</param>
    /// <returns>The output, or <see langword="null"/> when no output contains the point.</returns>
    public ClientOutput? At(double x, double y) => Layout.OutputAt(x, y) as ClientOutput;

    /// <summary>
    /// Destroys every tracked output and clears the list. It does not raise <see cref="Changed"/>.
    /// </summary>
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
