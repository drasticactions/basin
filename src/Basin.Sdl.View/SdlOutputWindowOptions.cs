namespace Basin.Sdl;

public sealed record SdlOutputWindowOptions
{
    public string Title { get; init; } = "basin";

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 800;

    public string? OutputName { get; init; }

    public bool Resizable { get; init; } = true;

    public double? Scale { get; init; }

    public bool GrabKeyboard { get; init; }

    public SdlWindow? PopupParent { get; init; }

    public int PopupX { get; init; }

    public int PopupY { get; init; }

    public bool Tooltip { get; init; }

    public bool Transparent { get; init; }

    public bool Borderless { get; init; }
}
