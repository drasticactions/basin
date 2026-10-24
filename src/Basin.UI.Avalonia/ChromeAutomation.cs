using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Basin.UI.Avalonia;

/// <summary>
/// Attached properties that let a screen reader read and press chrome that pointer gestures drive.
/// A control with <see cref="IsButtonProperty"/> set reads as a button whatever its type, and the reader's press raises <see cref="InvokedEvent"/> on it.
/// The control also takes keyboard focus, and Enter or Space raises <see cref="InvokedEvent"/>.
/// </summary>
public static class ChromeAutomation
{
    /// <summary>
    /// Identifies the attached property that makes a control read as a button.
    /// </summary>
    public static readonly AttachedProperty<bool> IsButtonProperty =
        AvaloniaProperty.RegisterAttached<Interactive, bool>("IsButton", typeof(ChromeAutomation));

    /// <summary>
    /// Identifies the attached event that a screen reader's press raises on a control with <see cref="IsButtonProperty"/> set.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> InvokedEvent =
        RoutedEvent.Register<RoutedEventArgs>("Invoked", RoutingStrategies.Bubble, typeof(ChromeAutomation));

    static ChromeAutomation()
    {
        IsButtonProperty.Changed.AddClassHandler<InputElement>(OnIsButtonChanged);
    }

    /// <summary>
    /// Gets a value indicating whether a control reads as a button.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <returns><see langword="true"/> when the control reads as a button.</returns>
    public static bool GetIsButton(Interactive control) => control.GetValue(IsButtonProperty);

    /// <summary>
    /// Sets a value indicating whether a control reads as a button.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="value"><see langword="true"/> to read the control as a button.</param>
    public static void SetIsButton(Interactive control, bool value) => control.SetValue(IsButtonProperty, value);

    /// <summary>
    /// Adds a handler for the <c>Invoked</c> attached event.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="handler">The handler.</param>
    public static void AddInvokedHandler(Interactive control, EventHandler<RoutedEventArgs> handler) => control.AddHandler(InvokedEvent, handler);

    /// <summary>
    /// Removes a handler for the <c>Invoked</c> attached event.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="handler">The handler.</param>
    public static void RemoveInvokedHandler(Interactive control, EventHandler<RoutedEventArgs> handler) => control.RemoveHandler(InvokedEvent, handler);

    /// <summary>
    /// Raises the <c>Invoked</c> attached event on a control, as a screen reader's press does.
    /// </summary>
    /// <param name="control">The control.</param>
    public static void Invoke(Interactive control) => control.RaiseEvent(new RoutedEventArgs(InvokedEvent, control));

    private static void OnIsButtonChanged(InputElement element, AvaloniaPropertyChangedEventArgs change)
    {
        var isButton = change.GetNewValue<bool>();
        element.Focusable = isButton;
        element.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        if (isButton)
        {
            element.AddHandler(InputElement.KeyDownEvent, OnKeyDown);
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || sender is not Interactive control || !ReferenceEquals(args.Source, control) || args.KeyModifiers != KeyModifiers.None
            || args.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        args.Handled = true;
        Invoke(control);
    }
}
