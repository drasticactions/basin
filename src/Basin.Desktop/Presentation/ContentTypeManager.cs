using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_content_type_manager_v1</c> global. It tracks the content type that each surface commits.
/// </summary>
public sealed class ContentTypeManager : IDisposable, IWpContentTypeManagerV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorAlreadyConstructed = 0;

    /// <summary>
    /// Lists the content types of the protocol, with the protocol's values.
    /// </summary>
    public enum ContentType : uint
    {
        /// <summary>
        /// No type is known.
        /// </summary>
        None = 0,
        /// <summary>
        /// Still images.
        /// </summary>
        Photo = 1,
        /// <summary>
        /// Moving images such as films and clips.
        /// </summary>
        Video = 2,
        /// <summary>
        /// Interactive content where latency matters.
        /// </summary>
        Game = 3,
    }

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, ContentType> _types = [];
    private readonly HashSet<Surface> _claimed = [];

    private readonly Basin.Capabilities.Defaults.SurfaceHints? _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentTypeManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global whose surfaces get a type.</param>
    /// <param name="hints">The surface hints that receive each type, or <see langword="null"/>.</param>
    public ContentTypeManager(
        WlServerDisplay display, CompositorGlobal compositor, Basin.Capabilities.Defaults.SurfaceHints? hints = null)
    {
        _compositor = compositor;
        _store = hints;
        _global = display.CreateGlobal(WpContentTypeManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a commit applies a different type, and with <see cref="ContentType.None"/> when the client destroys the type object.
    /// </summary>
    public event Action<Surface, ContentType>? TypeChanged;

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    /// <summary>
    /// Gets the type that the surface's last applied commit set.
    /// </summary>
    /// <param name="surface">The surface to look up, or <see langword="null"/>.</param>
    /// <returns>The type, or <see cref="ContentType.None"/> for a <see langword="null"/> surface or a surface without a type.</returns>
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

    void IWpContentTypeManagerV1Handler.Destroy(WpContentTypeManagerV1Resource resource)
    {
    }

    void IWpContentTypeManagerV1Handler.GetSurfaceContentType(WpContentTypeManagerV1Resource resource, in WpContentTypeManagerV1Resource.GetSurfaceContentTypeEventArgs args)
    {
        var handler = new SurfaceType(this);
        var typeResource = new WpContentTypeV1Resource(resource.Client, resource.Version, args.Id, handler);
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            return;
        }

        if (!_claimed.Add(surface))
        {
            resource.PostError(ErrorAlreadyConstructed, "The surface already has a content type object.");
            return;
        }

        handler.Attach(surface);
        typeResource.Destroyed += (_, _) =>
        {
            handler.Detach();
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
    }

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new WpContentTypeManagerV1Resource(client, version, id, this);

    private sealed class SurfaceType(ContentTypeManager owner) : IWpContentTypeV1Handler
    {
        private Surface? _surface;
        private ContentType? _pending;
        private Action? _onCommitRequested;
        private Action? _onCommitted;

        public void Attach(Surface surface)
        {
            _surface = surface;
            _onCommitRequested = OnCommitRequested;
            _onCommitted = OnCommitted;
            surface.CommitRequested += _onCommitRequested;
            surface.Committed += _onCommitted;
        }

        public void Detach()
        {
            if (_surface is { } surface)
            {
                surface.CommitRequested -= _onCommitRequested;
                surface.Committed -= _onCommitted;
            }
        }

        void IWpContentTypeV1Handler.Destroy(WpContentTypeV1Resource resource)
        {
        }

        void IWpContentTypeV1Handler.SetContentType(WpContentTypeV1Resource resource, in WpContentTypeV1Resource.SetContentTypeEventArgs args)
        {
            if (_surface is not null)
            {
                _pending = (ContentType)args.ContentType;
            }
        }

        private void OnCommitRequested()
        {
            if (_pending is { } type)
            {
                _pending = null;
                _surface!.Pending.SetExtension(new PendingType(type));
            }
        }

        private void OnCommitted()
        {
            if (_surface!.Current.TakeExtension<PendingType>() is { } carried)
            {
                using (carried)
                {
                    owner.Apply(_surface, carried.Type);
                }
            }
        }
    }
}
