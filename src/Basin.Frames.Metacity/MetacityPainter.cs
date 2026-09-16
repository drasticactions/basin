using Basin.Capabilities;

namespace Basin.Frames.Metacity;

public sealed partial class MetacityPainter
{
    public MetacityPainter(MetacityTheme theme, MetacityPalette palette, MetacityButtonLayout buttonLayout, MetacityFont font, MetacityResources resources)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(buttonLayout);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(resources);
        Theme = theme;
        Palette = palette;
        ButtonLayout = buttonLayout;
        Font = font;
        Resources = resources;
        _colors = new SkiaSharp.SKColor[theme.ColorSpecs.Count];
        _paletteVersion = -1;
    }

    private readonly SkiaSharp.SKColor[] _colors;
    private int _paletteVersion;

    public MetacityTheme Theme { get; }

    public MetacityPalette Palette { get; }

    public MetacityButtonLayout ButtonLayout { get; }

    public MetacityFont Font { get; }

    public MetacityResources Resources { get; }

    internal ReadOnlySpan<SkiaSharp.SKColor> Colors
    {
        get
        {
            if (_paletteVersion != Palette.Version)
            {
                for (var i = 0; i < _colors.Length; i++)
                {
                    _colors[i] = new SkiaSharp.SKColor(Theme.ColorSpecs[i].Resolve(Palette).ToArgb32());
                }

                _paletteVersion = Palette.Version;
            }

            return _colors;
        }
    }

    internal MetacityFrameStyle? StyleFor(in MetacityFrameInput input) =>
        Theme.GetStyle(input.Type, input.FrameStateKind, MetacityResize.Both, input.Focus);

    public FrameInsets Measure(in MetacityFrameInput input)
    {
        if (StyleFor(input) is not { } style)
        {
            return default;
        }

        return Borders(style.Layout, input);
    }

    private FrameInsets Borders(MetacityFrameLayout layout, in MetacityFrameInput input)
    {
        if (input.State.Fullscreen)
        {
            return default;
        }

        var textHeight = layout.HasTitle ? Font.TextHeight(layout.TitleScale) : 0;
        var buttonsHeight = layout.ButtonHeight + layout.ButtonBorder.Top + layout.ButtonBorder.Bottom;
        var titleHeight = textHeight + layout.TitleVerticalPad + layout.TitleBorder.Top + layout.TitleBorder.Bottom;
        var top = Math.Max(buttonsHeight, titleHeight);
        var bottom = input.State.Shaded ? 0 : layout.BottomHeight;
        return new FrameInsets(top, layout.RightWidth, bottom, layout.LeftWidth);
    }

    public bool Layout(in MetacityFrameInput input, int clientWidth, int clientHeight, MetacityFrameGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (StyleFor(input) is not { } style)
        {
            return false;
        }

        Layout(style.Layout, input, clientWidth, clientHeight, geometry);
        return true;
    }

    private void Layout(MetacityFrameLayout layout, in MetacityFrameInput input, int clientWidth, int clientHeight, MetacityFrameGeometry geometry)
    {
        var borders = Borders(layout, input);
        geometry.Clear();
        geometry.Borders = borders;
        var width = clientWidth + borders.Left + borders.Right;
        var height = (input.State.Shaded ? 0 : clientHeight) + borders.Top + borders.Bottom;
        geometry.Width = width;
        geometry.Height = height;
        geometry.TopTitlebarEdge = layout.TitleBorder.Top;
        geometry.BottomTitlebarEdge = layout.TitleBorder.Bottom;
        geometry.LeftTitlebarEdge = layout.LeftTitlebarEdge;
        geometry.RightTitlebarEdge = layout.RightTitlebarEdge;

        int buttonWidth;
        int buttonHeight;
        if (layout.ButtonSizing == MetacityButtonSizing.Aspect)
        {
            buttonHeight = borders.Top - layout.ButtonBorder.Top - layout.ButtonBorder.Bottom;
            buttonWidth = (int)(buttonHeight / layout.ButtonAspect);
        }
        else
        {
            buttonWidth = layout.ButtonWidth;
            buttonHeight = layout.ButtonHeight;
        }

        Span<MetacityButtonType> left = stackalloc MetacityButtonType[MetacityButtonLayout.MaxButtonsPerCorner];
        Span<MetacityButtonType> right = stackalloc MetacityButtonType[MetacityButtonLayout.MaxButtonsPerCorner];
        Span<bool> leftSpacer = stackalloc bool[MetacityButtonLayout.MaxButtonsPerCorner];
        Span<bool> rightSpacer = stackalloc bool[MetacityButtonLayout.MaxButtonsPerCorner];
        var nLeft = 0;
        var nRight = 0;
        var nLeftSpacers = 0;
        var nRightSpacers = 0;

        if (!layout.HideButtons)
        {
            for (var i = 0; i < ButtonLayout.LeftCount; i++)
            {
                var function = ButtonLayout.Left[i];
                if (Shown(function, input))
                {
                    left[nLeft] = MetacityFrameGeometry.TypeOf(function);
                    leftSpacer[nLeft] = ButtonLayout.LeftSpacer[i];
                    if (leftSpacer[nLeft])
                    {
                        nLeftSpacers++;
                    }

                    nLeft++;
                }
            }

            for (var i = 0; i < ButtonLayout.RightCount; i++)
            {
                var function = ButtonLayout.Right[i];
                if (Shown(function, input))
                {
                    right[nRight] = MetacityFrameGeometry.TypeOf(function);
                    rightSpacer[nRight] = ButtonLayout.RightSpacer[i];
                    if (rightSpacer[nRight])
                    {
                        nRightSpacers++;
                    }

                    nRight++;
                }
            }
        }

        while (nLeft > 0 || nRight > 0)
        {
            var spaceAvailable = width - layout.LeftTitlebarEdge - layout.RightTitlebarEdge;
            var used = buttonWidth * nLeft
                + (int)(0.75 * (buttonWidth * nLeftSpacers))
                + layout.ButtonBorder.Left * nLeft
                + layout.ButtonBorder.Right * nLeft
                + buttonWidth * nRight
                + (int)(0.75 * (buttonWidth * nRightSpacers))
                + layout.ButtonBorder.Left * nRight
                + layout.ButtonBorder.Right * nRight;
            if (used <= spaceAvailable)
            {
                break;
            }

            if (nLeftSpacers > 0)
            {
                leftSpacer[--nLeftSpacers] = false;
                continue;
            }

            if (nRightSpacers > 0)
            {
                rightSpacer[--nRightSpacers] = false;
                continue;
            }

            if (Strip(left, leftSpacer, ref nLeft, MetacityButtonType.Above) || Strip(right, rightSpacer, ref nRight, MetacityButtonType.Above)
                || Strip(left, leftSpacer, ref nLeft, MetacityButtonType.Stick) || Strip(right, rightSpacer, ref nRight, MetacityButtonType.Stick)
                || Strip(left, leftSpacer, ref nLeft, MetacityButtonType.Shade) || Strip(right, rightSpacer, ref nRight, MetacityButtonType.Shade)
                || Strip(left, leftSpacer, ref nLeft, MetacityButtonType.Minimize) || Strip(right, rightSpacer, ref nRight, MetacityButtonType.Minimize)
                || Strip(left, leftSpacer, ref nLeft, MetacityButtonType.Maximize) || Strip(right, rightSpacer, ref nRight, MetacityButtonType.Maximize)
                || Strip(left, leftSpacer, ref nLeft, MetacityButtonType.Close) || Strip(right, rightSpacer, ref nRight, MetacityButtonType.Close)
                || Strip(right, rightSpacer, ref nRight, MetacityButtonType.Menu) || Strip(left, leftSpacer, ref nLeft, MetacityButtonType.Menu)
                || Strip(right, rightSpacer, ref nRight, MetacityButtonType.AppMenu) || Strip(left, leftSpacer, ref nLeft, MetacityButtonType.AppMenu)
                || Strip(left, leftSpacer, ref nLeft, MetacityButtonType.Unabove) || Strip(right, rightSpacer, ref nRight, MetacityButtonType.Unabove)
                || Strip(left, leftSpacer, ref nLeft, MetacityButtonType.Unstick) || Strip(right, rightSpacer, ref nRight, MetacityButtonType.Unstick)
                || Strip(left, leftSpacer, ref nLeft, MetacityButtonType.Unshade) || Strip(right, rightSpacer, ref nRight, MetacityButtonType.Unshade))
            {
                continue;
            }

            break;
        }

        geometry.LeftCount = nLeft;
        geometry.RightCount = nRight;
        for (var i = 0; i < nLeft; i++)
        {
            geometry.LeftFunctions[i] = FunctionOf(left[i]);
        }

        for (var i = 0; i < nRight; i++)
        {
            geometry.RightFunctions[i] = FunctionOf(right[i]);
        }

        var buttonY = (borders.Top - (buttonHeight + layout.ButtonBorder.Top + layout.ButtonBorder.Bottom)) / 2 + layout.ButtonBorder.Top;
        var x = width - layout.RightTitlebarEdge;
        for (var i = nRight - 1; i >= 0; i--)
        {
            if (x < 0)
            {
                break;
            }

            var visibleX = x - layout.ButtonBorder.Right - buttonWidth;
            if (rightSpacer[i])
            {
                visibleX -= (int)(0.75 * buttonWidth);
            }

            var visible = new Box(visibleX, buttonY, buttonWidth, buttonHeight);
            var clickable = visible;
            if (input.AtEdge && i == nRight - 1 && (input.State.Maximized || input.State.Tiled == FrameTiling.Right))
            {
                clickable = new Box(
                    visible.X,
                    0,
                    buttonWidth + layout.RightTitlebarEdge + layout.RightWidth + layout.ButtonBorder.Right,
                    buttonY + buttonHeight);
            }

            ref var space = ref geometry.Buttons[(int)right[i]];
            space.Visible = visible;
            space.Clickable = clickable;
            SetRightBackground(geometry, i, nRight, visible);
            x = visible.X - layout.ButtonBorder.Left;
        }

        var titleRightEdge = x - layout.TitleBorder.Right;

        x = layout.LeftTitlebarEdge;
        for (var i = 0; i < nLeft; i++)
        {
            var visible = new Box(x + layout.ButtonBorder.Left, buttonY, buttonWidth, buttonHeight);
            var clickable = visible;
            if (input.AtEdge && i == 0 && (input.State.Maximized || input.State.Tiled == FrameTiling.Left))
            {
                clickable = new Box(
                    0,
                    0,
                    buttonWidth + layout.LeftTitlebarEdge + layout.LeftWidth + layout.ButtonBorder.Left,
                    buttonY + buttonHeight);
            }

            ref var space = ref geometry.Buttons[(int)left[i]];
            space.Visible = visible;
            space.Clickable = clickable;
            SetLeftBackground(geometry, i, nLeft, visible);
            x = visible.Right + layout.ButtonBorder.Right;
            if (leftSpacer[i])
            {
                x += (int)(0.75 * buttonWidth);
            }
        }

        var titleX = x + layout.TitleBorder.Left;
        var titleWidth = titleRightEdge - titleX;
        var titleHeight = borders.Top - layout.TitleBorder.Top - layout.TitleBorder.Bottom;
        if (titleWidth < 0 || titleHeight < 0)
        {
            titleWidth = 0;
            titleHeight = 0;
        }

        geometry.TitleRect = new Box(titleX, layout.TitleBorder.Top, titleWidth, titleHeight);

        var minForRounding = input.State.Shaded ? 0 : 5;
        geometry.TopLeftRadius = borders.Top + borders.Left >= minForRounding ? layout.TopLeftRadius : 0;
        geometry.TopRightRadius = borders.Top + borders.Right >= minForRounding ? layout.TopRightRadius : 0;
        geometry.BottomLeftRadius = borders.Bottom + borders.Left >= minForRounding ? layout.BottomLeftRadius : 0;
        geometry.BottomRightRadius = borders.Bottom + borders.Right >= minForRounding ? layout.BottomRightRadius : 0;
    }

    private static void SetLeftBackground(MetacityFrameGeometry geometry, int i, int n, in Box rect)
    {
        if (n == 1)
        {
            geometry.LeftSingleBackground = rect;
        }
        else if (i == 0)
        {
            geometry.LeftLeftBackground = rect;
        }
        else if (i == n - 1)
        {
            geometry.LeftRightBackground = rect;
        }
        else
        {
            geometry.LeftMiddleBackgrounds[i - 1] = rect;
        }
    }

    private static void SetRightBackground(MetacityFrameGeometry geometry, int i, int n, in Box rect)
    {
        if (n == 1)
        {
            geometry.RightSingleBackground = rect;
        }
        else if (i == n - 1)
        {
            geometry.RightRightBackground = rect;
        }
        else if (i == 0)
        {
            geometry.RightLeftBackground = rect;
        }
        else
        {
            geometry.RightMiddleBackgrounds[i - 1] = rect;
        }
    }

    private static bool Strip(Span<MetacityButtonType> types, Span<bool> spacers, ref int count, MetacityButtonType toStrip)
    {
        for (var i = 0; i < count; i++)
        {
            if (types[i] != toStrip)
            {
                continue;
            }

            count--;
            for (var j = i; j < count; j++)
            {
                types[j] = types[j + 1];
                spacers[j] = spacers[j + 1];
            }

            return true;
        }

        return false;
    }

    private static bool Shown(MetacityButtonFunction function, in MetacityFrameInput input) => function switch
    {
        MetacityButtonFunction.Shade => input.Allows(FrameCapabilities.Shade) && !input.State.Shaded,
        MetacityButtonFunction.Unshade => input.Allows(FrameCapabilities.Shade) && input.State.Shaded,
        MetacityButtonFunction.Above => input.Allows(FrameCapabilities.Above) && !input.State.Above,
        MetacityButtonFunction.Unabove => input.Allows(FrameCapabilities.Above) && input.State.Above,
        MetacityButtonFunction.Stick => input.Allows(FrameCapabilities.Stick) && !input.State.Sticky,
        MetacityButtonFunction.Unstick => input.Allows(FrameCapabilities.Stick) && input.State.Sticky,
        MetacityButtonFunction.Menu => input.Allows(FrameCapabilities.WindowMenu),
        MetacityButtonFunction.Minimize => input.Allows(FrameCapabilities.Minimize),
        MetacityButtonFunction.Maximize => input.Allows(FrameCapabilities.Maximize),
        MetacityButtonFunction.Close => true,
        _ => false,
    };

    private static MetacityButtonFunction FunctionOf(MetacityButtonType type) => type switch
    {
        MetacityButtonType.Shade => MetacityButtonFunction.Shade,
        MetacityButtonType.Above => MetacityButtonFunction.Above,
        MetacityButtonType.Stick => MetacityButtonFunction.Stick,
        MetacityButtonType.Unshade => MetacityButtonFunction.Unshade,
        MetacityButtonType.Unabove => MetacityButtonFunction.Unabove,
        MetacityButtonType.Unstick => MetacityButtonFunction.Unstick,
        MetacityButtonType.Menu => MetacityButtonFunction.Menu,
        MetacityButtonType.AppMenu => MetacityButtonFunction.AppMenu,
        MetacityButtonType.Minimize => MetacityButtonFunction.Minimize,
        MetacityButtonType.Maximize => MetacityButtonFunction.Maximize,
        _ => MetacityButtonFunction.Close,
    };
}
