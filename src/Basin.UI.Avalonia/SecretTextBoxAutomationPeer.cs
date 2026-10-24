using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Basin.UI.Avalonia;

/// <summary>
/// An automation peer that reports a masked value for a <see cref="SecretTextBox"/> and refuses writes while it is masked.
/// </summary>
public class SecretTextBoxAutomationPeer : TextBoxAutomationPeer, IValueProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SecretTextBoxAutomationPeer"/> class.
    /// </summary>
    /// <param name="owner">The text box that the peer reports.</param>
    public SecretTextBoxAutomationPeer(SecretTextBox owner)
        : base(owner)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the owner has a password character. The value then shows only that character, once per text character.
    /// </summary>
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

    /// <summary>
    /// Raises a value change for a text change, with masked old and new values. Other property changes raise nothing.
    /// </summary>
    /// <param name="sender">The owner.</param>
    /// <param name="e">The property change.</param>
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
