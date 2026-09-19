namespace Basin.Shell.Nested;

public sealed record ShellSettings(
    string Theme = ShellSettings.DefaultTheme,
    string ButtonLayout = ShellSettings.DefaultButtonLayout,
    string Palette = ShellSettings.DefaultPalette,
    double FontSize = ShellSettings.DefaultFontSize,
    string Background = ShellSettings.DefaultBackground,
    int Workspaces = ShellSettings.DefaultWorkspaces,
    int WorkspaceRows = 1,
    IReadOnlyList<string>? WorkspaceNames = null,
    FocusMode FocusMode = FocusMode.Click,
    FocusNewWindows FocusNewWindows = FocusNewWindows.Smart,
    PlacementMode Placement = PlacementMode.Automatic,
    bool CenterNewWindows = true,
    bool RaiseOnClick = true,
    bool AutoRaise = false,
    int AutoRaiseDelay = ShellSettings.DefaultAutoRaiseDelay,
    string MouseButtonModifier = ShellSettings.DefaultMouseButtonModifier,
    bool ResizeWithRightButton = true,
    TitlebarAction DoubleClickTitlebar = TitlebarAction.ToggleMaximize,
    TitlebarAction MiddleClickTitlebar = TitlebarAction.Lower,
    TitlebarAction RightClickTitlebar = TitlebarAction.Menu,
    bool Tiling = true,
    bool TopTiling = true,
    IReadOnlyList<ShellKey>? Keys = null)
{
    public const string DefaultTheme = "Atlanta";

    public const string DefaultButtonLayout = "menu:minimize,maximize,close";

    public const string DefaultPalette = "light";

    public const double DefaultFontSize = 13;

    public const string DefaultBackground = "#5891ad";

    public const int DefaultWorkspaces = 4;

    public const int DefaultAutoRaiseDelay = 500;

    public const string DefaultMouseButtonModifier = "Alt";

    public IReadOnlyList<string> WorkspaceNames { get; init; } = WorkspaceNames ?? [];

    public IReadOnlyList<ShellKey> Keys { get; init; } = Keys ?? [];
}
