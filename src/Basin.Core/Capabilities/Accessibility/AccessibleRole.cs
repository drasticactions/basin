namespace Basin.Capabilities;

/// <summary>
/// The role of a node that chrome without its own accessibility tree describes.
/// </summary>
public enum AccessibleRole
{
    /// <summary>
    /// A top-level window, such as a frame or a panel.
    /// </summary>
    Window,

    /// <summary>
    /// A container that groups other nodes.
    /// </summary>
    Group,

    /// <summary>
    /// A push button.
    /// </summary>
    Button,

    /// <summary>
    /// Static text.
    /// </summary>
    Label,

    /// <summary>
    /// A section heading.
    /// </summary>
    Heading,

    /// <summary>
    /// A check box or a switch.
    /// </summary>
    CheckBox,

    /// <summary>
    /// A choice from a list that shows one item.
    /// </summary>
    ComboBox,

    /// <summary>
    /// A slider over a numeric range.
    /// </summary>
    Slider,

    /// <summary>
    /// A text field.
    /// </summary>
    TextInput,

    /// <summary>
    /// A list.
    /// </summary>
    List,

    /// <summary>
    /// An item of a list.
    /// </summary>
    ListItem,

    /// <summary>
    /// A menu item.
    /// </summary>
    MenuItem,

    /// <summary>
    /// An image.
    /// </summary>
    Image,
}
