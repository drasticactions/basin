using Basin.Capabilities;
using Basin.UI.Quill;
using Pixman;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Basin.Frames.Quill;

/// <summary>
/// A server-side frame look that Quill draws. It has an opaque, a frosted and a glass mode.
/// </summary>
/// <remarks>
/// The renderer keeps the layout and the glass animation state of the last drawn frame. Give each window its own instance.
/// </remarks>
public sealed class QuillFrameRenderer : IFrameRenderer
{
    private readonly QuillFrameTheme _base;
    private QuillFrameTheme _theme;
    private readonly QuillFrameGeometry _geometry = new();
    private readonly List<(QuillFrameTheme Theme, QuillFrameMenu Menu)> _menus = [];
    private readonly HashSet<(Canvas Canvas, FontFile Face)> _prepared = [];
    private bool _laidOut;
    private bool _shaded;
    private double _glintX;
    private double _glintY;
    private double _glintStrength;
    private long _glintAt;
    private bool _glintResting = true;
    private double _emphasis = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuillFrameRenderer"/> class.
    /// </summary>
    /// <param name="theme">The theme for every color scheme that has no theme of its own.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public QuillFrameRenderer(QuillFrameTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _base = theme;
        _theme = theme;
    }

    /// <summary>
    /// Gets the surface type that the renderer draws into: <see cref="IQuillUISurface"/>.
    /// </summary>
    public Type SurfaceContract => typeof(IQuillUISurface);

    /// <summary>
    /// Gets the theme that the constructor took.
    /// </summary>
    public QuillFrameTheme Theme => _base;

    /// <summary>
    /// Gets or sets the theme for a window with the light color scheme. When it is <see langword="null"/>, the window uses <see cref="Theme"/>.
    /// The frame, the menu, the insets and the hit test all use it, so its geometry can differ from <see cref="Theme"/>.
    /// </summary>
    public QuillFrameTheme? LightTheme { get; set; }

    /// <summary>
    /// Gets or sets the theme for a window with the dark color scheme. When it is <see langword="null"/>, the window uses <see cref="Theme"/>.
    /// The frame, the menu, the insets and the hit test all use it, so its geometry can differ from <see cref="Theme"/>.
    /// </summary>
    public QuillFrameTheme? DarkTheme { get; set; }

    private QuillFrameTheme ThemeFor(FrameColorScheme scheme) => scheme switch
    {
        FrameColorScheme.Light => LightTheme ?? _base,
        FrameColorScheme.Dark => DarkTheme ?? _base,
        _ => _base,
    };

    private QuillFrameMenu MenuFor(FrameColorScheme scheme)
    {
        var theme = ThemeFor(scheme);
        foreach (var (owner, menu) in _menus)
        {
            if (ReferenceEquals(owner, theme))
            {
                return menu;
            }
        }

        var created = new QuillFrameMenu(theme);
        _menus.Add((theme, created));
        return created;
    }

    /// <summary>
    /// Gets the layout of the last drawn frame. The renderer changes the same instance on each draw.
    /// </summary>
    public QuillFrameGeometry Geometry => _geometry;

    /// <summary>
    /// Gets a value indicating whether every pixel of the last drawn frame is opaque. It is <see langword="false"/> before the first draw, and with rounded corners, a shaded window, frost or glass.
    /// </summary>
    public bool OpaqueChrome => _laidOut && !_geometry.HasRoundedCorner && !_shaded && !_theme.Frosted && !_theme.Glass;

    /// <summary>
    /// Fills the rounded frame area for a backdrop effect. Only a frosted theme that is not glass asks for one, and only after the first draw.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="into">The region to fill in surface coordinates. The renderer clears it first.</param>
    /// <returns><see langword="true"/> when the frame wants a backdrop effect.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is <see langword="null"/>.</exception>
    public bool BackdropRegion(in FrameState state, double scale, PixmanRegion32 into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (!_theme.Frosted || _theme.Glass || !_laidOut)
        {
            return false;
        }

        var radius = (int)Math.Round(Math.Max(0f, _theme.CornerRadius));
        return RoundedRegion.Fill(into, _geometry.Width, _geometry.Height, radius);
    }

    /// <summary>
    /// Describes the last drawn frame as a glass shape, with the client hole, the edge band and the pointer highlight. Only a glass theme gives a shape.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="shape">The shape when the call returns <see langword="true"/>, otherwise the default value.</param>
    /// <returns><see langword="true"/> when the theme is glass and a frame was drawn.</returns>
    public bool BackdropShape(in FrameState state, double scale, out FrameBackdropShape shape)
    {
        if (!_theme.Glass || !_laidOut)
        {
            shape = default;
            return false;
        }

        var w = _geometry.Width;
        var h = _geometry.Height;
        var border = _geometry.Border;
        var top = border + _geometry.TitleHeight;
        shape = new FrameBackdropShape
        {
            Width = w,
            Height = h,
            CornerRadius = Math.Max(0f, _theme.CornerRadius),
            EdgeWidth = Math.Max(0f, _theme.Bevel),
            ClientHole = _shaded || h - top - border <= 0 ? default : new Box(border, top, w - (2 * border), h - top - border),
            ClientRadius = _theme.ResolvedContentRadius,
            HighlightX = _glintX,
            HighlightY = _glintY,
            HighlightStrength = _glintStrength,
            Emphasis = _emphasis,
        };
        return true;
    }

    /// <summary>
    /// Tells whether a glass fade or the pointer highlight is still moving. An opaque or frosted theme never animates.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="interaction">The pointer state and the change times.</param>
    /// <returns><see langword="true"/> while a glass fade runs or the highlight has not reached the pointer.</returns>
    public bool Animating(in FrameState state, in FrameInteraction interaction)
    {
        if (!_theme.Glass)
        {
            return false;
        }

        var duration = Duration();
        var now = interaction.Now;
        return Running(interaction.ActiveChangedAt, now, duration)
            || Running(interaction.HoverChangedAt, now, duration)
            || Running(interaction.PressChangedAt, now, duration)
            || !GlintSettled(interaction);
    }

    /// <summary>
    /// Measures the frame with the theme of the state's color scheme.
    /// </summary>
    /// <param name="state">The window state. Its color scheme selects the theme.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The frame insets in logical pixels.</returns>
    public FrameInsets Measure(in FrameState state, double scale)
    {
        var theme = ThemeFor(state.ColorScheme);
        return new FrameInsets(
            theme.Border + theme.TitleHeight,
            theme.Border,
            state.Shaded ? 0 : theme.Border,
            theme.Border);
    }

    /// <inheritdoc/>
    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        _theme = ThemeFor(state.ColorScheme);
        ArgumentNullException.ThrowIfNull(surface);
        var quill = (IQuillUISurface)surface;
        Layout(clientBox, state);
        if (_theme.Glass)
        {
            var focus = Ease(interaction.ActiveChangedAt, interaction.Now, Duration());
            _emphasis = interaction.ActiveChangedAt == 0 ? (state.Active ? 1 : 0) : state.Active ? focus : 1 - focus;
            FollowGlint(interaction);
        }

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

    /// <summary>
    /// Finds the frame part at a point of the last drawn layout. The corner zone comes from the theme of the state's color scheme.
    /// </summary>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    /// <param name="state">The window state. Its color scheme selects the theme.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The part, or <see cref="FramePart.None"/> outside the frame or before the first draw.</returns>
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
        var zone = ThemeFor(state.ColorScheme).CornerZone;
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

    /// <inheritdoc/>
    public Box PartBounds(FramePart part)
    {
        var bounds = _geometry.BoundsOf(part);
        if (!_theme.Glass || bounds.IsEmpty || part is FramePart.Title or FramePart.Icon or FramePart.Menu)
        {
            return bounds;
        }

        return new Box(bounds.X - GlowReach, bounds.Y - GlowReach, bounds.Width + (2 * GlowReach), bounds.Height + (2 * GlowReach));
    }

    private const int GlowReach = 4;

    /// <inheritdoc/>
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

    /// <summary>
    /// Measures the window menu with the theme of the state's color scheme.
    /// </summary>
    /// <param name="state">The window state. It selects the items and the theme.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The menu size in logical pixels at <paramref name="scale"/>.</returns>
    public UISurfaceSize MeasureMenu(in FrameState state, double scale) => MenuFor(state.ColorScheme).Measure(state, scale);

    /// <summary>
    /// Draws the window menu with the theme and the font of the state's color scheme.
    /// </summary>
    /// <param name="surface">The menu surface. It must be an <see cref="IQuillUISurface"/>.</param>
    /// <param name="state">The window state. It selects the items, their labels and the theme.</param>
    /// <param name="hotItem">The item under the pointer, or -1 for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public void DrawMenu(IUISurface surface, in FrameState state, int hotItem)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var quill = (IQuillUISurface)surface;
        MenuFor(state.ColorScheme).Draw(quill, Face(ThemeFor(state.ColorScheme)), state, hotItem);
    }

    /// <summary>
    /// Finds the window menu item at a point with the theme of the state's color scheme.
    /// </summary>
    /// <param name="x">The horizontal position in logical pixels.</param>
    /// <param name="y">The vertical position in logical pixels.</param>
    /// <param name="state">The window state. It selects the items and the theme.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The item index, or -1 when no item is at the point.</returns>
    public int MenuItemAt(double x, double y, in FrameState state, double scale) => MenuFor(state.ColorScheme).ItemAt(x, y, state, scale);

    /// <summary>
    /// Gives the action of a window menu item for the state's color scheme.
    /// </summary>
    /// <param name="item">The item index.</param>
    /// <param name="state">The window state. It selects the items and the theme.</param>
    /// <returns>The action, or <see langword="null"/> when the index is out of range.</returns>
    public FrameAction? MenuItemAction(int item, in FrameState state) => MenuFor(state.ColorScheme).ActionOf(item, state);

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
        var face = Face(_theme);
        if (!_prepared.Add((canvas, face)))
        {
            return;
        }

        QuillFonts.AddFallbacks(canvas, face);
    }

    private static FontFile Face(QuillFrameTheme theme) => theme.Face ?? QuillFrameFonts.Bundled();

    private void Paint(Canvas canvas, in FrameState state, in FrameInteraction interaction)
    {
        if (!_laidOut)
        {
            return;
        }

        if (_theme.Glass)
        {
            PaintGlass(canvas, state, interaction);
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

    private void PaintGlass(Canvas canvas, in FrameState state, in FrameInteraction interaction)
    {
        var w = _geometry.Width;
        var h = _geometry.Height;
        var radius = _theme.CornerRadius;
        var outline = Mix(_theme.OutlineInactive, _theme.Outline, _emphasis);
        var text = Mix(_theme.TextInactive, _theme.Text, _emphasis);
        canvas.SetStrokeColor(outline);
        canvas.SetStrokeWidth(1);
        canvas.BeginPath();
        canvas.RoundedRect(0.5f, 0.5f, w - 1f, h - 1f, radius, radius, radius, radius);
        canvas.Stroke();

        PaintIcon(canvas, state, text);
        PaintGlassButton(canvas, _geometry.Close, FramePart.Close, interaction, _theme.Close, false);
        PaintGlassButton(canvas, _geometry.Maximize, FramePart.Maximize, interaction, _theme.Maximize, false);
        PaintGlassButton(canvas, _geometry.Minimize, FramePart.Minimize, interaction, _theme.Minimize, false);
        PaintGlassButton(canvas, _geometry.Shade, FramePart.Shade, interaction, _theme.Toggle, state.Shaded);
        PaintGlassButton(canvas, _geometry.Above, FramePart.Above, interaction, _theme.Toggle, state.Above);
        PaintGlassButton(canvas, _geometry.Stick, FramePart.Stick, interaction, _theme.Toggle, state.Sticky);
        PaintTitle(canvas, state, text);
    }

    private void PaintGlassButton(
        Canvas canvas, in Box box, FramePart part, in FrameInteraction interaction, Color32 lit, bool on)
    {
        if (box.IsEmpty)
        {
            return;
        }

        var duration = Duration();
        var hover = PartEase(part, interaction.Hot, interaction.PreviousHot, interaction.HoverChangedAt, interaction.Now, duration);
        var press = PartEase(part, interaction.Pressed, interaction.PreviousPressed, interaction.PressChangedAt, interaction.Now, duration);
        var radius = box.Width / 2f;
        var centerX = box.X + box.Width / 2f;
        var centerY = box.Y + box.Height / 2f;
        if (hover > 0)
        {
            canvas.CircleFilled(centerX, centerY, radius + (3f * (float)hover), WithAlpha(lit, 0x50 * hover));
        }

        var fill = Mix(_theme.Dormant, lit, Math.Max(on ? 1 : 0, Math.Max(hover, press)) * (0.6 + (0.4 * _emphasis)));
        canvas.CircleFilled(centerX, centerY, radius - (1.5f * (float)press), fill);
        var mark = Math.Max(hover, press);
        if (mark <= 0)
        {
            return;
        }

        canvas.SetStrokeColor(WithAlpha(_theme.Glyph, _theme.Glyph.A * mark));
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

    private void FollowGlint(in FrameInteraction interaction)
    {
        var target = interaction.PointerInside ? 1.0 : 0.0;
        var from = _glintResting ? Math.Max(_glintAt, interaction.PointerChangedAt) : _glintAt;
        var elapsed = _glintAt == 0 ? 0 : Math.Max(0, interaction.Now - from);
        _glintAt = interaction.Now;
        if (target > 0 && _glintStrength < 0.01)
        {
            _glintX = interaction.PointerX;
            _glintY = interaction.PointerY;
        }

        var follow = 1 - Math.Exp(-elapsed / (Duration() / 3.0));
        _glintStrength += (target - _glintStrength) * follow;
        if (Math.Abs(target - _glintStrength) < 0.01)
        {
            _glintStrength = target;
        }

        if (target > 0)
        {
            _glintX += (interaction.PointerX - _glintX) * follow;
            _glintY += (interaction.PointerY - _glintY) * follow;
            if (Math.Abs(interaction.PointerX - _glintX) < 0.5 && Math.Abs(interaction.PointerY - _glintY) < 0.5)
            {
                _glintX = interaction.PointerX;
                _glintY = interaction.PointerY;
            }
        }

        _glintResting = GlintSettled(interaction);
    }

    private bool GlintSettled(in FrameInteraction interaction)
    {
        var target = interaction.PointerInside ? 1.0 : 0.0;
        if (_glintStrength != target)
        {
            return false;
        }

        return target == 0 || (_glintX == interaction.PointerX && _glintY == interaction.PointerY);
    }

    private double Duration() => Math.Max(1, _theme.AnimationMs) * 1_000_000.0;

    private static bool Running(long at, long now, double duration) => at != 0 && now - at < duration;

    private static double Ease(long at, long now, double duration)
    {
        if (at == 0)
        {
            return 1;
        }

        var t = Math.Clamp((now - at) / duration, 0, 1);
        return t * t * (3 - (2 * t));
    }

    private static double PartEase(FramePart part, FramePart now, FramePart before, long at, long time, double duration)
    {
        var eased = Ease(at, time, duration);
        return part == now ? eased : part == before ? 1 - eased : 0;
    }

    private static Color32 Mix(Color32 from, Color32 to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Color32(
            (byte)Math.Round(from.R + ((to.R - from.R) * t)),
            (byte)Math.Round(from.G + ((to.G - from.G) * t)),
            (byte)Math.Round(from.B + ((to.B - from.B) * t)),
            (byte)Math.Round(from.A + ((to.A - from.A) * t)));
    }

    private static Color32 WithAlpha(Color32 color, double alpha) =>
        new(color.R, color.G, color.B, (byte)Math.Clamp(Math.Round(alpha), 0, 255));

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
            Face(_theme),
            0f,
            new Float2(0f, 0.5f));
        canvas.RestoreState();
    }

    private Color32 Veil(Color32 color, bool frost) =>
        frost ? new Color32(color.R, color.G, color.B, _theme.FrostAlpha) : color;

    private static bool Hits(in Box box, double x, double y) =>
        !box.IsEmpty && x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom;
}
