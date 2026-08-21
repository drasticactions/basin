using Basin;
using Basin.Avalonia;
using Basin.XWayland;

namespace Waylonia;

internal static class WayloniaXWayland
{
    public static IProtocolModule? TryCreateModule() => new XWaylandModule();

    public static void Attach(IProtocolModule module, BasinCompositorHost host, ToplevelWindows windows)
    {
        if (module is not XWaylandModule xwayland)
        {
            return;
        }

        xwayland.WindowManagerReady += wm =>
        {
            wm.WindowMapped += window => Add(windows, window, overrideRedirect: false);
            wm.OverrideRedirectMapped += window => Add(windows, window, overrideRedirect: true);
        };
    }

    public static string? DisplayName(BasinCompositorHost host) =>
        host.Services.Find<XWaylandServer>()?.DisplayName;

    private static void Add(ToplevelWindows windows, XWaylandWindow window, bool overrideRedirect)
    {
        if (window.Surface is null)
        {
            return;
        }

        var adapter = new XForeignWindow(window, overrideRedirect);
        var id = windows.AddForeign(adapter);
        adapter.Removed += () => windows.RemoveForeign(id);
    }

    private sealed class XForeignWindow : IForeignToplevel
    {
        private readonly XWaylandWindow _window;

        internal XForeignWindow(XWaylandWindow window, bool overrideRedirect)
        {
            _window = window;
            IsPopup = overrideRedirect;
            window.TitleChanged += () => TitleChanged?.Invoke();
            window.GeometryChanged += () => GeometryChanged?.Invoke();
            window.Unmapped += OnGone;
            window.Destroyed += OnGone;
        }

        private bool _gone;

        internal event Action? Removed;

        private void OnGone()
        {
            if (!_gone)
            {
                _gone = true;
                Closed?.Invoke();
                Removed?.Invoke();
            }
        }

        public Surface Surface => _window.Surface!;

        public string Title => _window.Title;

        public string AppId => _window.Class;

        public int Width => _window.Width;

        public int Height => _window.Height;

        public bool ServerDecorated => !IsPopup && _window.WantsDecorations;

        public bool IsPopup { get; }

        public Surface? AnchorSurface => _window.TransientFor?.Surface;

        public int AnchorOffsetX => _window.X - (_window.TransientFor?.X ?? 0);

        public int AnchorOffsetY => _window.Y - (_window.TransientFor?.Y ?? 0);

        public event Action? TitleChanged;

        public event Action? GeometryChanged;

        public event Action? Closed;

        public void Resize(int width, int height) => _window.Configure(_window.X, _window.Y, width, height);

        public void Close() => _window.Close();

        public void Activate(bool active)
        {
            if (active)
            {
                _window.Activate();
                _window.Raise();
            }
        }
    }
}
