using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace Basin.UI.Avalonia;

public class SecretTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override AutomationPeer OnCreateAutomationPeer() => new SecretTextBoxAutomationPeer(this);
}
