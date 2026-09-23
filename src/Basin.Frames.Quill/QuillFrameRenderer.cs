using Basin.Capabilities;
using Basin.UI.Quill;
using Pixman;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Basin.Frames.Quill;

public sealed class QuillFrameRenderer : IFrameRenderer
{
    private readonly QuillFrameTheme _theme;
    private readonly QuillFrameGeometry _geometry = new();
    private readonly QuillFrameMenu _menu;
    private readonly HashSet<Canvas> _prepared = [];
    private FontFile? _face;
    private bool _laidOut;
    private bool _shaded;

    public QuillFrameRenderer(QuillFrameTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        _menu = new QuillFrameMenu(theme);
    }

    public Type SurfaceContract => typeof(IQuillUISurface);

    public QuillFrameTheme Theme => _theme;

    public QuillFrameGeometry Geometry => _geometry;

    public bool OpaqueChrome => _laidOut && !_geometry.HasRoundedCorner && !_shaded && !_theme.Frosted;

    public bool BackdropRegion(in FrameState state, double scale, PixmanRegion32 into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (!_theme.Frosted || !_laidOut)
        {
            return false;
        }

        var width = _geometry.Width;
        var height = _geometry.Height;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var radius = (int)Math.Round(Math.Min(_theme.CornerRadius, Math.Min(width, height) / 2.0));
        if (radius <= 0)
        {
            into.UnionRect(into, 0, 0, (uint)width, (uint)height);
            return true;
        }

        for (var y = 0; y < radius; y++)
        {
            AddCornerRow(into, width, y, Inset(radius, y));
            AddCornerRow(into, width, height - 1 - y, Inset(radius, y));
        }

        var straight = height - (2 * radius);
        if (straight > 0)
        {
            into.UnionRect(into, 0, radius, (uint)width, (uint)straight);
        }

        return !into.IsEmpty;
    }

    private static int Inset(int radius, int row)
    {
        var dy = radius - row - 0.5;
        return (int)Math.Round(radius - Math.Sqrt((radius * radius) - (dy * dy)));
    }

    private static void AddCornerRow(PixmanRegion32 into, int width, int y, int inset)
    {
        var run = width - (2 * inset);
        if (run > 0 && y >= 0)
        {
            into.UnionRect(into, inset, y, (uint)run, 1);
        }
    }

    public FrameInsets Measure(in FrameState state, double scale) => new(
        _theme.Border + _theme.TitleHeight,
        _theme.Border,
        state.Shaded ? 0 : _theme.Border,
        _theme.Border);

    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var quill = (IQuillUISurface)surface;
        Layout(clientBox, state);
        var canvas = quill.BeginDraw();
        try
        {
            Prepare(canvas);
            Paint(canvas, state, interaction);
        }
        finally
        {
            quill.EndDraw();
        }
    }

    public FramePart PartAt(double x, double y, in FrameState state, double scale)
    {
        if (!_laidOut)
        {
            return FramePart.None;
        }

        var w = _geometry.Width;
        var h = _geometry.Height;
        if (w <= 0 || h <= 0 || x < 0 || y < 0 || x >= w || y >= h)
        {
            return FramePart.None;
        }

        var border = _geometry.Border;
        var zone = _theme.CornerZone;
        var onEdge = x < border || x >= w - border || y < border || y >= h - border;
        if (onEdge)
        {
            var left = x < zone;
            var right = x >= w - zone;
            var top = y < zone;
            var bottom = y >= h - zone;
            if (top && left)
            {
                return FramePart.TopLeft;
            }

            if (top && right)
            {
                return FramePart.TopRight;
            }

            if (bottom && left)
            {
                return FramePart.BottomLeft;
            }

            if (bottom && right)
            {
                return FramePart.BottomRight;
            }

            return y < border ? FramePart.Top
                : y >= h - border ? FramePart.Bottom
                : x < border ? FramePart.Left
                : FramePart.Right;
        }

        if (y < border + _geometry.TitleHeight)
        {
            for (var part = FramePart.Icon; part <= FramePart.Stick; part++)
            {
                if (Hits(_geometry.BoundsOf(part), x, y))
                {
                    return part;
                }
            }

            return FramePart.Title;
        }

        return FramePart.Border;
    }

    public Box PartBounds(FramePart part) => _geometry.BoundsOf(part);

    public string? CursorFor(FramePart part) => part switch
    {
        FramePart.Top => "top_side",
        FramePart.Bottom => "bottom_side",
        FramePart.Left => "left_side",
        FramePart.Right => "right_side",
        FramePart.TopLeft => "top_left_corner",
        FramePart.TopRight => "top_right_corner",
        FramePart.BottomLeft => "bottom_left_corner",
        FramePart.BottomRight => "bottom_right_corner",
        _ => null,
    };

    public UISurfaceSize MeasureMenu(in FrameState state, double scale) => _menu.Measure(state, scale);

    public void DrawMenu(IUISurface surface, in FrameState state, int hotItem)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var quill = (IQuillUISurface)surface;
        _menu.Draw(quill, Face(), state, hotItem);
    }

    public int MenuItemAt(double x, double y, in FrameState state, double scale) => _menu.ItemAt(x, y, state, scale);

    public FrameAction? MenuItemAction(int item, in FrameState state) => _menu.ActionOf(item, state);

    private void Layout(in Box clientBox, in FrameState state)
    {
        var border = _theme.Border;
        var bottom = state.Shaded ? 0 : border;
        var title = _theme.TitleHeight;
        var width = clientBox.Width + 2 * border;
        var height = clientBox.Height + title + border + bottom;

        _geometry.Reset();
        _geometry.Width = width;
        _geometry.Height = height;
        _geometry.Border = border;
        _geometry.TitleHeight = title;
        _geometry.HasRoundedCorner = _theme.CornerRadius > 0f;

        var side = _theme.ButtonSize;
        var centerY = border + (title - side) / 2;
        var cursor = width - border - _theme.ButtonGap - side;
        var room = width - 2 * border - _theme.IconSize - 3 * _theme.ButtonGap;

        _geometry.Close = Take(ref cursor, ref room, centerY, side, true);
        _geometry.Maximize = Take(ref cursor, ref room, centerY, side, state.Capabilities.HasFlag(FrameCapabilities.Maximize));
        _geometry.Minimize = Take(ref cursor, ref room, centerY, side, state.Capabilities.HasFlag(FrameCapabilities.Minimize));
        _geometry.Shade = Take(ref cursor, ref room, centerY, side, _theme.Toggles && state.Capabilities.HasFlag(FrameCapabilities.Shade));
        _geometry.Above = Take(ref cursor, ref room, centerY, side, _theme.Toggles && state.Capabilities.HasFlag(FrameCapabilities.Above));
        _geometry.Stick = Take(ref cursor, ref room, centerY, side, _theme.Toggles && state.Capabilities.HasFlag(FrameCapabilities.Stick));

        var iconSide = _theme.IconSize;
        _geometry.Icon = new Box(
            border + _theme.ButtonGap, border + (title - iconSide) / 2, iconSide, iconSide);
        if (state.Capabilities.HasFlag(FrameCapabilities.WindowMenu))
        {
            _geometry.Menu = _geometry.Icon;
            _geometry.Icon = default;
        }

        var anchor = _geometry.Menu.IsEmpty ? _geometry.Icon : _geometry.Menu;
        var titleLeft = anchor.Right + _theme.ButtonGap;
        var titleRight = cursor + side;
        _geometry.Title = titleRight > titleLeft
            ? new Box(titleLeft, border, titleRight - titleLeft, title)
            : default;

        _laidOut = width > 0 && height > 0;
        _shaded = state.Shaded;
    }

    private Box Take(ref int cursor, ref int room, int y, int side, bool wanted)
    {
        if (!wanted || room < side + _theme.ButtonGap)
        {
            return default;
        }

        var box = new Box(cursor, y, side, side);
        cursor -= side + _theme.ButtonGap;
        room -= side + _theme.ButtonGap;
        return box;
    }

    private void Prepare(Canvas canvas)
    {
        if (!_prepared.Add(canvas))
        {
            return;
        }

        QuillFonts.AddFallbacks(canvas, Face());
    }

    private FontFile Face() => _face ??= _theme.Face ?? QuillFrameFonts.Bundled();

    private void Paint(Canvas canvas, in FrameState state, in FrameInteraction interaction)
    {
        if (!_laidOut)
        {
            return;
        }

        var w = _geometry.Width;
        var h = _geometry.Height;
        var border = _geometry.Border;
        var title = _geometry.TitleHeight;
        var radius = _theme.CornerRadius;
        var body = state.Active ? _theme.Body : _theme.BodyInactive;
        var outline = state.Active ? _theme.Outline : _theme.OutlineInactive;
        var text = state.Active ? _theme.Text : _theme.TextInactive;

        var frost = _theme.Frosted;
        var titleBottom = border + title;
        canvas.SetFillColor(Veil(body, frost));
        if (frost)
        {
            canvas.SetAntiAlias(false);
        }

        canvas.BeginPath();
        if (frost)
        {
            canvas.RoundedRect(0, titleBottom, w, Math.Max(0, h - titleBottom), 0, 0, radius, radius);
        }
        else
        {
            canvas.RoundedRect(0, 0, w, h, radius, radius, radius, radius);
        }

        canvas.Fill();
        if (frost)
        {
            canvas.SetAntiAlias(true);
        }

        canvas.SetLinearBrush(
            0, 0, 0, titleBottom,
            Veil(state.Active ? _theme.TitleTop : _theme.TitleTopInactive, frost),
            Veil(state.Active ? _theme.TitleBottom : _theme.TitleBottomInactive, frost));
        if (frost)
        {
            canvas.SetAntiAlias(false);
        }

        canvas.BeginPath();
        canvas.RoundedRect(0, 0, w, titleBottom, radius, radius, 0, 0);
        canvas.Fill();
        if (frost)
        {
            canvas.SetAntiAlias(true);
        }
        canvas.ClearBrush();

        canvas.SetStrokeColor(outline);
        canvas.SetStrokeWidth(1);
        canvas.BeginPath();
        canvas.RoundedRect(0.5f, 0.5f, w - 1f, h - 1f, radius, radius, radius, radius);
        canvas.Stroke();

        PaintIcon(canvas, state, text);
        PaintButton(canvas, _geometry.Close, FramePart.Close, interaction, _theme.Close, state.Active);
        PaintButton(canvas, _geometry.Maximize, FramePart.Maximize, interaction, _theme.Maximize, state.Active);
        PaintButton(canvas, _geometry.Minimize, FramePart.Minimize, interaction, _theme.Minimize, state.Active);
        PaintButton(canvas, _geometry.Shade, FramePart.Shade, interaction, _theme.Toggle, state.Active && state.Shaded);
        PaintButton(canvas, _geometry.Above, FramePart.Above, interaction, _theme.Toggle, state.Active && state.Above);
        PaintButton(canvas, _geometry.Stick, FramePart.Stick, interaction, _theme.Toggle, state.Active && state.Sticky);
        PaintTitle(canvas, state, text);
    }

    private void PaintIcon(Canvas canvas, in FrameState state, Color32 text)
    {
        var box = _geometry.Menu.IsEmpty ? _geometry.Icon : _geometry.Menu;
        if (box.IsEmpty)
        {
            return;
        }

        var side = Math.Min(box.Width, box.Height) / 2f;
        canvas.SetFillColor(text);
        canvas.BeginPath();
        canvas.RoundedRect(
            box.X + (box.Width - side) / 2f, box.Y + (box.Height - side) / 2f, side, side, side / 3f);
        canvas.Fill();
    }

    private void PaintButton(
        Canvas canvas, in Box box, FramePart part, in FrameInteraction interaction, Color32 lit, bool on)
    {
        if (box.IsEmpty)
        {
            return;
        }

        var hot = interaction.Hot == part;
        var pressed = interaction.Pressed == part;
        var color = hot || pressed || on ? lit : _theme.Dormant;
        var radius = box.Width / 2f;
        var centerX = box.X + box.Width / 2f;
        var centerY = box.Y + box.Height / 2f;
        canvas.CircleFilled(centerX, centerY, pressed ? radius - 1f : radius, color);
        if (!hot && !pressed)
        {
            return;
        }

        canvas.SetStrokeColor(_theme.Glyph);
        canvas.SetStrokeWidth(1.4f);
        var arm = radius * 0.42f;
        canvas.BeginPath();
        switch (part)
        {
            case FramePart.Close:
                canvas.MoveTo(centerX - arm, centerY - arm);
                canvas.LineTo(centerX + arm, centerY + arm);
                canvas.MoveTo(centerX - arm, centerY + arm);
                canvas.LineTo(centerX + arm, centerY - arm);
                break;
            case FramePart.Minimize:
                canvas.MoveTo(centerX - arm, centerY);
                canvas.LineTo(centerX + arm, centerY);
                break;
            default:
                canvas.MoveTo(centerX - arm, centerY - arm);
                canvas.LineTo(centerX + arm, centerY - arm);
                canvas.LineTo(centerX + arm, centerY + arm);
                canvas.LineTo(centerX - arm, centerY + arm);
                canvas.ClosePath();
                break;
        }

        canvas.Stroke();
    }

    private void PaintTitle(Canvas canvas, in FrameState state, Color32 text)
    {
        var box = _geometry.Title;
        var label = string.IsNullOrEmpty(state.Title) ? state.AppId : state.Title;
        if (box.IsEmpty || string.IsNullOrEmpty(label))
        {
            return;
        }

        canvas.SaveState();
        canvas.IntersectScissor(box.X, box.Y, box.Width, box.Height);
        canvas.DrawText(
            label,
            box.X,
            box.Y + box.Height / 2f,
            text,
            _theme.FontSize,
            Face(),
            0f,
            new Float2(0f, 0.5f));
        canvas.RestoreState();
    }

    private Color32 Veil(Color32 color, bool frost) =>
        frost ? new Color32(color.R, color.G, color.B, _theme.FrostAlpha) : color;

    private static bool Hits(in Box box, double x, double y) =>
        !box.IsEmpty && x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom;
}
