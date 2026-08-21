using Wayland;
using Wayland.Server;

namespace Basin;

public sealed class FixesGlobal : IDisposable
{
    public const int Version = 2;

    private readonly WlServerDisplay _display;
    private readonly WlGlobal? _global;

    public FixesGlobal(WlServerDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        if (display.SupportsFixes)
        {
            _global = display.CreateGlobal(WlFixes.Interface, Version, OnBind);
        }
    }

    public bool IsPublished => _global is not null;

    public void Dispose() => _global?.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var fixes = new WlFixesResource(client, version, id);

        fixes.DestroyRegistry += (_, e) => _display.DestroyRegistry(client, e.RegistryHandle);
        fixes.AckGlobalRemove += (_, e) =>
            _display.AckGlobalRemove(client, fixes.RawHandle, e.RegistryHandle, e.Name);
    }
}
