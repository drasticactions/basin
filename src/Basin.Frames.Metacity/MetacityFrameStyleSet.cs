namespace Basin.Frames.Metacity;

internal sealed class MetacityFrameStyleSet(MetacityFrameStyleSet? parent)
{
    public MetacityFrameStyleSet? Parent { get; } = parent;

    public MetacityFrameStyle?[,] Normal { get; } = new MetacityFrameStyle?[(int)MetacityResize.Count, (int)MetacityFocus.Count];

    public MetacityFrameStyle?[,] Shaded { get; } = new MetacityFrameStyle?[(int)MetacityResize.Count, (int)MetacityFocus.Count];

    public MetacityFrameStyle?[] Maximized { get; } = new MetacityFrameStyle?[(int)MetacityFocus.Count];

    public MetacityFrameStyle?[] TiledLeft { get; } = new MetacityFrameStyle?[(int)MetacityFocus.Count];

    public MetacityFrameStyle?[] TiledRight { get; } = new MetacityFrameStyle?[(int)MetacityFocus.Count];

    public MetacityFrameStyle?[] MaximizedAndShaded { get; } = new MetacityFrameStyle?[(int)MetacityFocus.Count];

    public MetacityFrameStyle?[] TiledLeftAndShaded { get; } = new MetacityFrameStyle?[(int)MetacityFocus.Count];

    public MetacityFrameStyle?[] TiledRightAndShaded { get; } = new MetacityFrameStyle?[(int)MetacityFocus.Count];

    public MetacityFrameStyle?[] FocusStyles(MetacityFrameStateKind state) => state switch
    {
        MetacityFrameStateKind.Maximized => Maximized,
        MetacityFrameStateKind.TiledLeft => TiledLeft,
        MetacityFrameStateKind.TiledRight => TiledRight,
        MetacityFrameStateKind.MaximizedAndShaded => MaximizedAndShaded,
        MetacityFrameStateKind.TiledLeftAndShaded => TiledLeftAndShaded,
        _ => TiledRightAndShaded,
    };

    public MetacityFrameStyle? GetStyle(MetacityFrameStateKind state, MetacityResize resize, MetacityFocus focus)
    {
        MetacityFrameStyle? style;
        if (state is MetacityFrameStateKind.Normal or MetacityFrameStateKind.Shaded)
        {
            style = state == MetacityFrameStateKind.Shaded
                ? Shaded[(int)resize, (int)focus]
                : Normal[(int)resize, (int)focus];
            if (style is null && Parent is not null)
            {
                style = Parent.GetStyle(state, resize, focus);
            }

            if (style is null && resize != MetacityResize.Both)
            {
                style = GetStyle(state, MetacityResize.Both, focus);
            }

            return style;
        }

        style = FocusStyles(state)[(int)focus];
        if (style is null)
        {
            if (state is MetacityFrameStateKind.TiledLeft or MetacityFrameStateKind.TiledRight)
            {
                style = GetStyle(MetacityFrameStateKind.Normal, resize, focus);
            }
            else if (state is MetacityFrameStateKind.TiledLeftAndShaded or MetacityFrameStateKind.TiledRightAndShaded)
            {
                style = GetStyle(MetacityFrameStateKind.Shaded, resize, focus);
            }
        }

        if (style is null && Parent is not null)
        {
            style = Parent.GetStyle(state, resize, focus);
        }

        return style;
    }

    public string? Validate()
    {
        for (var resize = MetacityResize.None; resize < MetacityResize.Count; resize++)
        {
            for (var focus = MetacityFocus.No; focus < MetacityFocus.Count; focus++)
            {
                if (GetStyle(MetacityFrameStateKind.Normal, resize, focus) is null)
                {
                    return Missing(MetacityFrameStateKind.Normal, resize, focus);
                }
            }
        }

        foreach (var state in new[] { MetacityFrameStateKind.Shaded, MetacityFrameStateKind.Maximized, MetacityFrameStateKind.MaximizedAndShaded })
        {
            for (var focus = MetacityFocus.No; focus < MetacityFocus.Count; focus++)
            {
                if (GetStyle(state, MetacityResize.None, focus) is null)
                {
                    return Missing(state, MetacityResize.None, focus);
                }
            }
        }

        return null;
    }

    private static string Missing(MetacityFrameStateKind state, MetacityResize resize, MetacityFocus focus) =>
        $"Missing <frame state=\"{MetacityThemeReader.FrameStateName(state)}\" resize=\"{MetacityThemeReader.ResizeName(resize)}\" focus=\"{MetacityThemeReader.FocusName(focus)}\" style=\"whatever\"/>";
}
