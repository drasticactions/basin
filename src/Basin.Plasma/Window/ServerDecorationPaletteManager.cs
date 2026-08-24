using Basin.Plasma.Protocol;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class ServerDecorationPaletteManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, SurfacePalette> _palettes = [];

    public ServerDecorationPaletteManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
        _global = display.CreateGlobal(OrgKdeKwinServerDecorationPaletteManager.Interface, Version, OnBind);
    }

    public event Action<Surface, string?>? PaletteChanged;

    public SurfacePalette? PaletteOf(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return _palettes.GetValueOrDefault(surface);
    }

    public void Dispose()
    {
        _palettes.Clear();
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdeKwinServerDecorationPaletteManagerResource(client, version, id);
        manager.Create += (_, e) =>
        {
            var resource = new OrgKdeKwinServerDecorationPaletteResource(client, manager.Version, e.Id);
            if (_compositor.ResolveSurface(e.Surface) is not { } surface)
            {
                return;
            }

            void Clear()
            {
                if (_palettes.Remove(surface))
                {
                    PaletteChanged?.Invoke(surface, null);
                }
            }

            resource.SetPalette += (_, pe) =>
            {
                _palettes[surface] = new SurfacePalette(surface, pe.Palette);
                PaletteChanged?.Invoke(surface, pe.Palette);
            };
            resource.Destroyed += (_, _) => Clear();
            surface.Destroyed += Clear;
        };
    }
}
