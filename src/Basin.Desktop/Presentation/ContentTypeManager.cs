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

    private readonly Basin.Capabilities.Defaults.SurfaceHints? _store;

    public ContentTypeManager(
        WlServerDisplay display, CompositorGlobal compositor, Basin.Capabilities.Defaults.SurfaceHints? hints = null)
    {
        _compositor = compositor;
        _store = hints;
        _global = display.CreateGlobal(WpContentTypeManagerV1.Interface, Version, OnBind);
    }

    public event Action<Surface, ContentType>? TypeChanged;

    public void Dispose() => _global.Dispose();

    public ContentType TypeOf(Surface? surface) =>
        surface is null ? ContentType.None : _types.GetValueOrDefault(surface, ContentType.None);

    private void Apply(Surface surface, ContentType type)
    {
        if (_types.GetValueOrDefault(surface, ContentType.None) == type)
        {
            return;
        }

        _types[surface] = type;
        _store?.SetContentType(surface, (Basin.Capabilities.SurfaceContentType)type);
        TypeChanged?.Invoke(surface, type);
    }

    private sealed class PendingType(ContentType type) : IDisposable
    {
        public ContentType Type { get; } = type;

        public void Dispose()
        {
        }
    }

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

            ContentType? pending = null;
            resource.SetContentType += (_, te) => pending = (ContentType)te.ContentType;
            Action onCommitRequested = () =>
            {
                if (pending is { } type)
                {
                    pending = null;
                    surface.Pending.SetExtension(new PendingType(type));
                }
            };
            Action onCommitted = () =>
            {
                if (surface.Current.TakeExtension<PendingType>() is { } carried)
                {
                    using (carried)
                    {
                        Apply(surface, carried.Type);
                    }
                }
            };
            surface.CommitRequested += onCommitRequested;
            surface.Committed += onCommitted;
            resource.Destroyed += (_, _) =>
            {
                surface.CommitRequested -= onCommitRequested;
                surface.Committed -= onCommitted;
                _claimed.Remove(surface);
                _store?.SetContentType(surface, Basin.Capabilities.SurfaceContentType.None);
                if (_types.Remove(surface))
                {
                    TypeChanged?.Invoke(surface, ContentType.None);
                }
            };
            surface.Destroyed += () =>
            {
                _claimed.Remove(surface);
                _types.Remove(surface);
                _store?.SetContentType(surface, Basin.Capabilities.SurfaceContentType.None);
            };
        };
    }
}
