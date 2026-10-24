namespace Basin.SoftKeyboard;

/// <summary>
/// The kind of the focused text field, mapped from the text-input content purpose.
/// </summary>
public enum FieldKind
{
    /// <summary>
    /// No kind. The default value.
    /// </summary>
    None,
    /// <summary>
    /// Normal, alpha or name text, or a purpose that has no other kind.
    /// </summary>
    Text,
    /// <summary>
    /// A password field.
    /// </summary>
    Password,
    /// <summary>
    /// An email address field.
    /// </summary>
    Email,
    /// <summary>
    /// A URL field.
    /// </summary>
    Url,
    /// <summary>
    /// A digits, number or PIN field.
    /// </summary>
    Number,
    /// <summary>
    /// A phone number field.
    /// </summary>
    Phone,
    /// <summary>
    /// A terminal.
    /// </summary>
    Terminal,
    /// <summary>
    /// A date, time or date-and-time field.
    /// </summary>
    Date,
}
