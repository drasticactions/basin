using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// The view model of the remote desktop prompt, which asks which input devices a session can control.
/// </summary>
public sealed class DevicePromptModel : PromptModel
{
    private bool _keyboard;
    private bool _pointer;
    private bool _touch;
    private bool _clipboard;
    private bool _persist;

    /// <summary>
    /// Initializes a new instance of the <see cref="DevicePromptModel"/> class. Each requested device and the clipboard start checked.
    /// </summary>
    /// <param name="prompt">The request that names the devices and the extra choices.</param>
    public DevicePromptModel(in DevicePrompt prompt)
        : base("Allow remote control?", prompt.AppId, prompt.DisplayName, prompt.IconPath)
    {
        OffersKeyboard = (prompt.Requested & InputDeviceCapability.Keyboard) != 0;
        OffersPointer = (prompt.Requested & InputDeviceCapability.Pointer) != 0;
        OffersTouch = (prompt.Requested & InputDeviceCapability.Touch) != 0;
        OffersClipboard = prompt.ClipboardRequested;
        OfferPersist = prompt.OfferPersist;
        _keyboard = OffersKeyboard;
        _pointer = OffersPointer;
        _touch = OffersTouch;
        _clipboard = OffersClipboard;
    }

    /// <summary>
    /// Gets a value indicating whether the request asked for the keyboard.
    /// </summary>
    public bool OffersKeyboard { get; }

    /// <summary>
    /// Gets a value indicating whether the request asked for the pointer.
    /// </summary>
    public bool OffersPointer { get; }

    /// <summary>
    /// Gets a value indicating whether the request asked for the touchscreen.
    /// </summary>
    public bool OffersTouch { get; }

    /// <summary>
    /// Gets a value indicating whether the request asked for clipboard sharing.
    /// </summary>
    public bool OffersClipboard { get; }

    /// <summary>
    /// Gets a value indicating whether the dialog shows the choice to remember the answer.
    /// </summary>
    public bool OfferPersist { get; }

    /// <summary>
    /// Gets the label of the deny button, which is <c>Cancel</c>.
    /// </summary>
    public override string DenyLabel => "Cancel";

    /// <summary>
    /// Gets or sets a value indicating whether the keyboard is checked. A change notifies the view.
    /// </summary>
    public bool Keyboard
    {
        get => _keyboard;
        set
        {
            _keyboard = value;
            Changed();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the pointer is checked. A change notifies the view.
    /// </summary>
    public bool Pointer
    {
        get => _pointer;
        set
        {
            _pointer = value;
            Changed();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the touchscreen is checked. A change notifies the view.
    /// </summary>
    public bool Touch
    {
        get => _touch;
        set
        {
            _touch = value;
            Changed();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether clipboard sharing is checked. A change notifies the view.
    /// </summary>
    public bool Clipboard
    {
        get => _clipboard;
        set
        {
            _clipboard = value;
            Changed();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user asked to remember the choice. A change notifies the view.
    /// </summary>
    public bool Persist
    {
        get => _persist;
        set
        {
            _persist = value;
            Changed();
        }
    }

    /// <summary>
    /// Gets the devices that are checked now.
    /// </summary>
    public InputDeviceCapability Devices =>
        (Keyboard ? InputDeviceCapability.Keyboard : 0) | (Pointer ? InputDeviceCapability.Pointer : 0) | (Touch ? InputDeviceCapability.Touch : 0);

    /// <summary>
    /// Gets a value indicating whether the accept command is allowed. It needs at least one checked device.
    /// </summary>
    protected override bool CanAccept => Devices != 0;
}
