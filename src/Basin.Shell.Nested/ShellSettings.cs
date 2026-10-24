namespace Basin.Shell.Nested;

/// <summary>
/// The settings of the nested shell. <see cref="NestedShell.Apply"/> applies a new instance while the shell runs.
/// </summary>
/// <param name="Theme">The Metacity theme name.</param>
/// <param name="ButtonLayout">The Metacity button layout text.</param>
/// <param name="Palette">The frame palette. The value <c>dark</c> selects the dark palette, and any other value the light one.</param>
/// <param name="FontSize">The title font size.</param>
/// <param name="Background">The background color as <c>#rgb</c> or <c>#rrggbb</c>. An invalid value gives <see cref="DefaultBackground"/>.</param>
/// <param name="Workspaces">The number of workspaces. A value below 1 counts as 1.</param>
/// <param name="WorkspaceRows">The number of rows in the workspace grid.</param>
/// <param name="WorkspaceNames">The workspace names in index order, or <see langword="null"/> for none.</param>
/// <param name="FocusMode">How the pointer moves focus.</param>
/// <param name="FocusNewWindows">How a new window can take focus.</param>
/// <param name="Placement">Where a new window goes.</param>
/// <param name="CenterNewWindows">A value indicating whether a window that fits nowhere is centered instead of cascaded.</param>
/// <param name="RaiseOnClick">A value indicating whether a click on a window raises it.</param>
/// <param name="AutoRaise">A value indicating whether a window that gets focus from the pointer is raised after a delay.</param>
/// <param name="AutoRaiseDelay">The auto-raise delay in milliseconds.</param>
/// <param name="MouseButtonModifier">The modifier that lets a button drag a window from anywhere: <c>Super</c>, <c>Ctrl</c>, or any other value for Alt.</param>
/// <param name="ResizeWithRightButton">A value indicating whether the modifier and the right button resize a window.</param>
/// <param name="DoubleClickTitlebar">The action of a double click on a title bar.</param>
/// <param name="MiddleClickTitlebar">The action of a middle click on a title bar.</param>
/// <param name="RightClickTitlebar">The action of a right click on a title bar.</param>
/// <param name="Tiling">A value indicating whether a drag to the left or right edge tiles a window.</param>
/// <param name="TopTiling">A value indicating whether a drag to the top edge maximizes a window.</param>
/// <param name="Keys">The configured keys, or <see langword="null"/> for the defaults only.</param>
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
    /// <summary>
    /// The theme name when the settings name none.
    /// </summary>
    public const string DefaultTheme = "Atlanta";

    /// <summary>
    /// The button layout when the settings give none.
    /// </summary>
    public const string DefaultButtonLayout = "menu:minimize,maximize,close";

    /// <summary>
    /// The palette when the settings name none.
    /// </summary>
    public const string DefaultPalette = "light";

    /// <summary>
    /// The title font size when the settings give none.
    /// </summary>
    public const double DefaultFontSize = 13;

    /// <summary>
    /// The background color when the settings give none or an invalid one.
    /// </summary>
    public const string DefaultBackground = "#5891ad";

    /// <summary>
    /// The number of workspaces when the settings give none.
    /// </summary>
    public const int DefaultWorkspaces = 4;

    /// <summary>
    /// The auto-raise delay in milliseconds when the settings give none.
    /// </summary>
    public const int DefaultAutoRaiseDelay = 500;

    /// <summary>
    /// The window drag modifier when the settings name none.
    /// </summary>
    public const string DefaultMouseButtonModifier = "Alt";

    /// <summary>
    /// Gets the workspace names in index order. It is empty, not <see langword="null"/>, when none are configured.
    /// </summary>
    public IReadOnlyList<string> WorkspaceNames { get; init; } = WorkspaceNames ?? [];

    /// <summary>
    /// Gets the configured keys. It is empty, not <see langword="null"/>, when none are configured.
    /// </summary>
    public IReadOnlyList<ShellKey> Keys { get; init; } = Keys ?? [];
}
