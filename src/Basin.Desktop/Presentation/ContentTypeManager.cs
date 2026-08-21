using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class ContentTypeManager : IDisposable
{
    public const int Version = 1;

    private const uint ErrorAlreadyConstructed = 0;

    public enum ContentType : uint
    {
        None = 0,
        Photo = 1,
        Video = 2,
        Game = 3,
    }

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, ContentType> _types = [];
    private readonly HashSet<Surface> _claimed = [];

    public ContentTypeManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(WpContentTypeManagerV1.Interface, Version, OnBind);
    }

    public event Action<Surface, ContentType>? TypeChanged;

    public void Dispose() => _global.Dispose();

    public ContentType TypeOf(Surface? surface) =>
        surface is null ? ContentType.None : _types.GetValueOrDefault(surface, ContentType.None);

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpContentTypeManagerV1Resource(client, version, id);
        manager.GetSurfaceContentType += (_, e) =>
        {
            var resource = new WpContentTypeV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (!_claimed.Add(surface))
            {
                manager.PostError(ErrorAlreadyConstructed, "surface already has a content type object");
                return;
            }

            resource.SetContentType += (_, te) =>
            {
                _types[surface] = (ContentType)te.ContentType;
                TypeChanged?.Invoke(surface, (ContentType)te.ContentType);
            };
            resource.Destroyed += (_, _) =>
            {
                _claimed.Remove(surface);
                if (_types.Remove(surface))
                {
                    TypeChanged?.Invoke(surface, ContentType.None);
                }
            };
            surface.Destroyed += () =>
            {
                _claimed.Remove(surface);
                _types.Remove(surface);
            };
        };
    }
}
