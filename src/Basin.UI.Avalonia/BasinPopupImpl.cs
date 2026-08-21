using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Platform;

namespace Basin.UI.Avalonia;

internal sealed class BasinPopupImpl : BasinTopLevelImpl, IPopupImpl, IManagedPopupPositionerPopup
{
    private readonly BasinTopLevelImpl _parent;
    private readonly IScreenImpl _screens;

    public BasinPopupImpl(BasinPlatformContext context, BasinTopLevelImpl parent, IScreenImpl screens)
        : base(context)
    {
        _parent = parent;
        _screens = screens;
        PopupPositioner = new ManagedPopupPositioner(this);
        Resize(1, 1, parent.RenderScaling, WindowResizeReason.Layout);
    }

    public IPopupPositioner? PopupPositioner { get; }

    public Action<PixelPoint>? PositionChanged { get; set; }

    public Action? Deactivated { get; set; }

    public Action? Activated { get; set; }

    public PixelPoint Position => ScreenPosition;

    public Size MaxAutoSizeHint => new(4096, 4096);

    public IReadOnlyList<ManagedPopupPositionerScreenInfo> Screens =>
        _screens.AllScreens
            .Select(s => new ManagedPopupPositionerScreenInfo(
                s.Bounds.ToRect(1),
                s.WorkingArea.ToRect(1)))
            .ToArray();

    public Rect ParentClientAreaScreenGeometry => new(
        _parent.ScreenPosition.X,
        _parent.ScreenPosition.Y,
        _parent.ClientSize.Width * _parent.RenderScaling,
        _parent.ClientSize.Height * _parent.RenderScaling);

    public double Scaling => _parent.RenderScaling;

    public void MoveAndResize(global::Avalonia.Point devicePoint, Size virtualSize)
    {
        Resize(
            Math.Max(1, (int)Math.Ceiling(virtualSize.Width)),
            Math.Max(1, (int)Math.Ceiling(virtualSize.Height)),
            _parent.RenderScaling,
            WindowResizeReason.Layout);

        var position = new PixelPoint((int)Math.Round(devicePoint.X), (int)Math.Round(devicePoint.Y));
        ScreenPosition = position;
        Surface?.SetPositionFromToolkit(position.X / Scaling, position.Y / Scaling);
        PositionChanged?.Invoke(position);
    }

    public void Show(bool activate, bool isDialog)
    {
        Context.Host?.AnnouncePopup(Surface!);
        if (activate)
        {
            Activated?.Invoke();
        }
    }

    public void Hide()
    {
        Context.Host?.DismissPopup(Surface!);
        Deactivated?.Invoke();
    }

    public void Activate() => Activated?.Invoke();

    public void SetTopmost(bool value)
    {
    }

    public void SetWindowManagerAddShadowHint(bool enabled)
    {
    }

    public void TakeFocus()
    {
    }

    public override void Dispose()
    {
        if (!IsDisposed && Surface is { } surface)
        {
            Context.Host?.DismissPopup(surface);
        }

        base.Dispose();
    }
}
