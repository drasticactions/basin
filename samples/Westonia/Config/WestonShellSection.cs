using System.Globalization;

namespace Westonia;

public sealed class WestonShellSection
{
    public string? BackgroundImage { get; set; }

    public uint BackgroundColor { get; set; } = 0xFF002244;

    public BackgroundType BackgroundType { get; set; } = BackgroundType.Tile;

    public uint PanelColor { get; set; } = 0x90000000;

    public PanelPosition PanelPosition { get; set; } = PanelPosition.Top;

    public bool Locking { get; set; } = true;

    public ShellAnimation Animation { get; set; } = ShellAnimation.None;

    public ShellAnimation StartupAnimation { get; set; } = ShellAnimation.Fade;

    public ShellAnimation CloseAnimation { get; set; } = ShellAnimation.Fade;

    public ShellAnimation FocusAnimation { get; set; } = ShellAnimation.None;

    public string? Client { get; set; }

    public string BindingModifier { get; set; } = "super";

    public int NumWorkspaces { get; set; } = 1;

    public string? CursorTheme { get; set; }

    public int CursorSize { get; set; } = 24;

    public bool AllowZap { get; set; } = true;

    public ClockFormat ClockFormat { get; set; } = ClockFormat.Minutes;

    public bool DisallowOutputChangedMove { get; set; }
}
