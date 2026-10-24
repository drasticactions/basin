using Basin.Capabilities;
using Basin.UI.Quill;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Basin.Frames.Quill;

/// <summary>
/// The window menu of a Quill frame. It lists one item for each action that the window allows, and Close last.
/// </summary>
public sealed class QuillFrameMenu
{
    private readonly QuillFrameTheme _theme;
    private readonly List<FrameActionKind> _items = [];
    private FrameState _built;
    private bool _hasBuilt;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuillFrameMenu"/> class.
    /// </summary>
    /// <param name="theme">The theme that gives the menu sizes, colors and font size.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public QuillFrameMenu(QuillFrameTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
    }

    /// <summary>
    /// Gets the actions of the items for the last window state that the menu measured, drew or hit-tested.
    /// </summary>
    public IReadOnlyList<FrameActionKind> Items => _items;

    /// <summary>
    /// Measures the menu for a window state.
    /// </summary>
    /// <param name="state">The window state that selects the items.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The menu size in logical pixels at <paramref name="scale"/>.</returns>
    public UISurfaceSize Measure(in FrameState state, double scale)
    {
        var count = Build(state);
        return count == 0
            ? default
            : new UISurfaceSize(_theme.MenuWidth, count * _theme.MenuItemHeight + 2 * _theme.MenuPadding, scale);
    }

    /// <summary>
    /// Draws the menu into its popup surface.
    /// </summary>
    /// <param name="surface">The menu surface.</param>
    /// <param name="face">The font for the labels. When it is <see langword="null"/>, the menu draws no labels.</param>
    /// <param name="state">The window state that selects the items and their labels.</param>
    /// <param name="hotItem">The item under the pointer, or -1 for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Finds the item at a point on the menu surface.
    /// </summary>
    /// <param name="x">The horizontal position in logical pixels.</param>
    /// <param name="y">The vertical position in logical pixels.</param>
    /// <param name="state">The window state that selects the items.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The item index, or -1 when no item is at the point.</returns>
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

    /// <summary>
    /// Gives the action of an item.
    /// </summary>
    /// <param name="item">The item index.</param>
    /// <param name="state">The window state that selects the items.</param>
    /// <returns>The action, or <see langword="null"/> when the index is out of range.</returns>
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

        if (state.Capabilities.HasFlag(FrameCapabilities.X11Density))
        {
            _items.Add(FrameActionKind.ToggleX11Density);
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
        FrameActionKind.ToggleX11Density => state.LowDensity ? "Use full density" : "Use low density",
        _ => "Close",
    };
}
