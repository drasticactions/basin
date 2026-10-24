using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace Basin.UI.Avalonia;

/// <summary>
/// A <see cref="TextBox"/> that hides its text from accessibility clients while <see cref="TextBox.PasswordChar"/> is set.
/// </summary>
public class SecretTextBox : TextBox
{
    /// <summary>
    /// Gets the style key. It is <see cref="TextBox"/>, so the control uses the text box theme.
    /// </summary>
    protected override Type StyleKeyOverride => typeof(TextBox);

    /// <summary>
    /// Creates a <see cref="SecretTextBoxAutomationPeer"/> for the control.
    /// </summary>
    /// <returns>The new peer.</returns>
    protected override AutomationPeer OnCreateAutomationPeer() => new SecretTextBoxAutomationPeer(this);
}
