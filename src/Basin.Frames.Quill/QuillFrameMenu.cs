using Basin.Capabilities;
using Basin.UI.Quill;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Basin.Frames.Quill;

public sealed class QuillFrameMenu
{
    private readonly QuillFrameTheme _theme;
    private readonly List<FrameActionKind> _items = [];
    private FrameState _built;
    private bool _hasBuilt;

    public QuillFrameMenu(QuillFrameTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
    }

    public IReadOnlyList<FrameActionKind> Items => _items;

    public UISurfaceSize Measure(in FrameState state, double scale)
    {
        var count = Build(state);
        return count == 0
            ? default
            : new UISurfaceSize(_theme.MenuWidth, count * _theme.MenuItemHeight + 2 * _theme.MenuPadding, scale);
    }

    public void Draw(IQuillUISurface surface, FontFile? face, in FrameState state, int hotItem)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var count = Build(state);
        var width = _theme.MenuWidth;
        var height = count * _theme.MenuItemHeight + 2 * _theme.MenuPadding;
        var canvas = surface.BeginDraw();
        try
        {
            canvas.SetFillColor(_theme.MenuFill);
            canvas.BeginPath();
            canvas.RoundedRect(0, 0, width, height, 6);
            canvas.Fill();

            canvas.SetStrokeColor(_theme.Outline);
            canvas.SetStrokeWidth(1);
            canvas.BeginPath();
            canvas.RoundedRect(0.5f, 0.5f, width - 1f, height - 1f, 6);
            canvas.Stroke();

            for (var item = 0; item < count; item++)
            {
                var top = _theme.MenuPadding + item * _theme.MenuItemHeight;
                if (item == hotItem)
                {
                    canvas.SetFillColor(_theme.MenuHot);
                    canvas.BeginPath();
                    canvas.RoundedRect(
                        _theme.MenuPadding, top + 1,
                        width - 2 * _theme.MenuPadding, _theme.MenuItemHeight - 2, 4);
                    canvas.Fill();
                }

                if (face is not null)
                {
                    canvas.DrawText(
                        Label(_items[item], state),
                        _theme.MenuPadding + 10f,
                        top + (_theme.MenuItemHeight / 2f),
                        _theme.Text,
                        _theme.FontSize,
                        face,
                        0f,
                        new Float2(0f, 0.5f));
                }
            }
        }
        finally
        {
            surface.EndDraw();
        }
    }

    public int ItemAt(double x, double y, in FrameState state, double scale)
    {
        var count = Build(state);
        if (count == 0 || x < 0 || x >= _theme.MenuWidth || y < _theme.MenuPadding)
        {
            return -1;
        }

        var item = (int)((y - _theme.MenuPadding) / _theme.MenuItemHeight);
        return item >= 0 && item < count ? item : -1;
    }

    public FrameAction? ActionOf(int item, in FrameState state)
    {
        var count = Build(state);
        return item >= 0 && item < count ? new FrameAction(_items[item]) : null;
    }

    private int Build(in FrameState state)
    {
        if (_hasBuilt && _built == state)
        {
            return _items.Count;
        }

        _items.Clear();
        if (state.Capabilities.HasFlag(FrameCapabilities.Minimize))
        {
            _items.Add(FrameActionKind.Minimize);
        }

        if (state.Capabilities.HasFlag(FrameCapabilities.Maximize))
        {
            _items.Add(FrameActionKind.ToggleMaximize);
        }

        if (state.Capabilities.HasFlag(FrameCapabilities.Shade))
        {
            _items.Add(FrameActionKind.ToggleShade);
        }

        if (state.Capabilities.HasFlag(FrameCapabilities.Above))
        {
            _items.Add(FrameActionKind.ToggleAbove);
        }

        if (state.Capabilities.HasFlag(FrameCapabilities.Stick))
        {
            _items.Add(FrameActionKind.ToggleSticky);
        }

        _items.Add(FrameActionKind.Close);
        _built = state;
        _hasBuilt = true;
        return _items.Count;
    }

    private static string Label(FrameActionKind kind, in FrameState state) => kind switch
    {
        FrameActionKind.Minimize => "Minimize",
        FrameActionKind.ToggleMaximize => state.Maximized ? "Restore" : "Maximize",
        FrameActionKind.ToggleShade => state.Shaded ? "Unshade" : "Shade",
        FrameActionKind.ToggleAbove => state.Above ? "Not always on top" : "Always on top",
        FrameActionKind.ToggleSticky => state.Sticky ? "Only this workspace" : "Always on visible workspace",
        _ => "Close",
    };
}
