using AccessKit;
using Avalonia.Automation;
using Avalonia.Automation.Peers;

namespace Basin.UI.Avalonia;

internal static class AccessKitRoles
{
    public static Role? TryMap(AutomationControlType type) => type switch
    {
        AutomationControlType.None => Role.GenericContainer,
        AutomationControlType.Button => Role.Button,
        AutomationControlType.Calendar => Role.Grid,
        AutomationControlType.CheckBox => Role.CheckBox,
        AutomationControlType.ComboBox => Role.ComboBox,
        AutomationControlType.ComboBoxItem => Role.ListBoxOption,
        AutomationControlType.Edit => Role.TextInput,
        AutomationControlType.Hyperlink => Role.Link,
        AutomationControlType.Image => Role.Image,
        AutomationControlType.ListItem => Role.ListBoxOption,
        AutomationControlType.List => Role.ListBox,
        AutomationControlType.Menu => Role.Menu,
        AutomationControlType.MenuBar => Role.MenuBar,
        AutomationControlType.MenuItem => Role.MenuItem,
        AutomationControlType.ProgressBar => Role.ProgressIndicator,
        AutomationControlType.RadioButton => Role.RadioButton,
        AutomationControlType.ScrollBar => Role.ScrollBar,
        AutomationControlType.Slider => Role.Slider,
        AutomationControlType.Spinner => Role.SpinButton,
        AutomationControlType.StatusBar => Role.Status,
        AutomationControlType.Tab => Role.TabList,
        AutomationControlType.TabItem => Role.Tab,
        AutomationControlType.Text => Role.Label,
        AutomationControlType.ToolBar => Role.Toolbar,
        AutomationControlType.ToolTip => Role.Tooltip,
        AutomationControlType.Tree => Role.Tree,
        AutomationControlType.TreeItem => Role.TreeItem,
        AutomationControlType.Custom => Role.Unknown,
        AutomationControlType.Group => Role.Group,
        AutomationControlType.Thumb => Role.Button,
        AutomationControlType.DataGrid => Role.Grid,
        AutomationControlType.DataItem => Role.Row,
        AutomationControlType.Document => Role.Document,
        AutomationControlType.SplitButton => Role.Button,
        AutomationControlType.Window => Role.Window,
        AutomationControlType.Pane => Role.Pane,
        AutomationControlType.Header => Role.RowGroup,
        AutomationControlType.HeaderItem => Role.ColumnHeader,
        AutomationControlType.Table => Role.Table,
        AutomationControlType.TitleBar => Role.TitleBar,
        AutomationControlType.Separator => Role.Splitter,
        AutomationControlType.Expander => Role.Group,
        AutomationControlType.ScrollViewer => Role.ScrollView,
        _ => null,
    };

    public static Role RoleOf(AutomationPeer peer)
    {
        if (peer.GetHeadingLevel() > 0)
        {
            return Role.Heading;
        }
        var landmark = peer.GetLandmarkType() switch
        {
            AutomationLandmarkType.Banner => Role.Banner,
            AutomationLandmarkType.Complementary => Role.Complementary,
            AutomationLandmarkType.ContentInfo => Role.ContentInfo,
            AutomationLandmarkType.Region => Role.Region,
            AutomationLandmarkType.Form => Role.Form,
            AutomationLandmarkType.Main => Role.Main,
            AutomationLandmarkType.Navigation => Role.Navigation,
            AutomationLandmarkType.Search => Role.Search,
            _ => (Role?)null,
        };
        if (landmark is { } l)
        {
            return l;
        }
        return TryMap(peer.GetAutomationControlType()) ?? Role.GenericContainer;
    }
}
