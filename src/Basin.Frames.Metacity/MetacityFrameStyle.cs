namespace Basin.Frames.Metacity;

internal sealed class MetacityFrameStyle(MetacityFrameStyle? parent, MetacityFrameLayout layout)
{
    public MetacityFrameStyle? Parent { get; } = parent;

    public MetacityFrameLayout Layout { get; } = layout;

    public MetacityDrawOpList?[,] Buttons { get; } = new MetacityDrawOpList?[(int)MetacityButtonType.Count, (int)MetacityButtonState.Count];

    public MetacityDrawOpList?[] Pieces { get; } = new MetacityDrawOpList?[(int)MetacityPiece.Count];

    public MetacityColorSpec? WindowBackground { get; set; }

    public byte WindowBackgroundAlpha { get; set; } = 255;

    public MetacityDrawOpList? GetPiece(MetacityPiece piece)
    {
        for (var style = this; style is not null; style = style.Parent)
        {
            if (style.Pieces[(int)piece] is { } list)
            {
                return list;
            }
        }

        return null;
    }

    public MetacityDrawOpList? GetButton(MetacityButtonType type, MetacityButtonState state)
    {
        MetacityDrawOpList? list = null;
        for (var style = this; style is not null && list is null; style = style.Parent)
        {
            list = style.Buttons[(int)type, (int)state];
        }

        if (list is null && type == MetacityButtonType.LeftSingleBackground)
        {
            return GetButton(MetacityButtonType.LeftLeftBackground, state);
        }

        if (list is null && type == MetacityButtonType.RightSingleBackground)
        {
            return GetButton(MetacityButtonType.RightRightBackground, state);
        }

        if (list is null && type is MetacityButtonType.LeftLeftBackground or MetacityButtonType.LeftRightBackground)
        {
            return GetButton(MetacityButtonType.LeftMiddleBackground, state);
        }

        if (list is null && type is MetacityButtonType.RightLeftBackground or MetacityButtonType.RightRightBackground)
        {
            return GetButton(MetacityButtonType.RightMiddleBackground, state);
        }

        if (list is null && state == MetacityButtonState.Prelight)
        {
            return GetButton(type, MetacityButtonState.Normal);
        }

        return list;
    }

    public string? Validate(int requiredVersion)
    {
        for (var type = MetacityButtonType.Close; type < MetacityButtonType.Count; type++)
        {
            for (var state = MetacityButtonState.Normal; state < MetacityButtonState.Count; state++)
            {
                if (GetButton(type, state) is null && MetacityThemeReader.EarliestVersionWithButton(type) <= requiredVersion)
                {
                    return $"<button function=\"{MetacityThemeReader.ButtonTypeName(type)}\" state=\"{MetacityThemeReader.ButtonStateName(state)}\" draw_ops=\"whatever\"/> must be specified for this frame style";
                }
            }
        }

        return null;
    }
}
