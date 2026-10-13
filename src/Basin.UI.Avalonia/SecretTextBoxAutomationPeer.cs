using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Basin.UI.Avalonia;

public class SecretTextBoxAutomationPeer : TextBoxAutomationPeer, IValueProvider
{
    public SecretTextBoxAutomationPeer(SecretTextBox owner)
        : base(owner)
    {
    }

    public bool IsMasked => Owner.PasswordChar != default;

    bool IValueProvider.IsReadOnly => Owner.IsReadOnly;

    string? IValueProvider.Value => Expose(Owner.Text);

    void IValueProvider.SetValue(string? value)
    {
        if (!IsMasked)
        {
            Owner.Text = value;
        }
    }

    protected override void OwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
        {
            RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, Expose(e.OldValue as string), Expose(e.NewValue as string));
        }
    }

    private string? Expose(string? text) =>
        IsMasked && text is not null ? new string(Owner.PasswordChar, text.Length) : text;
}
