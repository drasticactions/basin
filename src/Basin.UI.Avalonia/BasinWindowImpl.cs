using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Platform;

namespace Basin.UI.Avalonia;

internal class BasinWindowImpl : BasinTopLevelImpl, IWindowImpl
{
    public BasinWindowImpl(BasinPlatformContext context)
        : base(context)
    {
    }

    public WindowState WindowState { get; set; }

    public bool WindowStateGetterIsUsable => false;

    public Action<WindowState>? WindowStateChanged { get; set; }

    public Action? GotInputWhenDisabled { get; set; }

    public Func<WindowCloseReason, bool>? Closing { get; set; }

    public Action<bool>? ExtendClientAreaToDecorationsChanged { get; set; }

    public Action<PixelPoint>? PositionChanged { get; set; }

    public Action? Deactivated { get; set; }

    public Action? Activated { get; set; }

    public PixelPoint Position => ScreenPosition;

    public Size MaxAutoSizeHint => new(4096, 4096);

    public bool IsClientAreaExtendedToDecorations => false;

    public bool NeedsManagedDecorations => false;

    public PlatformRequestedDrawnDecoration RequestedDrawnDecorations => default;

    public Thickness ExtendedMargins => default;

    public Thickness OffScreenMargin => default;

    public string? Title { get; private set; }

    public void Show(bool activate, bool isDialog)
    {
        if (activate)
        {
            Activated?.Invoke();
        }
    }

    public void Hide() => Deactivated?.Invoke();

    public void Activate() => Activated?.Invoke();

    public void SetTopmost(bool value)
    {
    }

    public void SetTitle(string? title) => Title = title;

    public void SetParent(IWindowImpl? parent)
    {
    }

    public void SetEnabled(bool enable)
    {
    }

    public void SetWindowDecorations(WindowDecorations enabled)
    {
    }

    public void SetIcon(IWindowIconImpl? icon)
    {
    }

    public void ShowTaskbarIcon(bool value)
    {
    }

    public void CanResize(bool value)
    {
    }

    public void SetCanMinimize(bool value)
    {
    }

    public void SetCanMaximize(bool value)
    {
    }

    public void BeginMoveDrag(PointerPressedEventArgs e) => Surface?.RequestMoveDrag();

    public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e) => Surface?.RequestResizeDrag(edge);

    public void Resize(Size clientSize, WindowResizeReason reason = WindowResizeReason.Application) =>
        Resize((int)Math.Round(clientSize.Width), (int)Math.Round(clientSize.Height), RenderScaling, reason);

    public void Move(PixelPoint point)
    {
        ScreenPosition = point;
        PositionChanged?.Invoke(point);
    }

    public void SetMinMaxSize(Size minSize, Size maxSize)
    {
    }

    public void SetExtendClientAreaToDecorationsHint(bool extendIntoClientAreaHint)
    {
    }

    public void SetExtendClientAreaTitleBarHeightHint(double titleBarHeight)
    {
    }

    public void SetShadowExtents(Thickness extents)
    {
    }
}
