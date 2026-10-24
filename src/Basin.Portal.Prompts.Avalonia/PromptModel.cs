using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// Base class for the view model of an Avalonia portal prompt. It supplies the title, the application line, the icon and the button commands.
/// The prompt completes once, through a command or <see cref="Complete"/>.
/// </summary>
public abstract class PromptModel : INotifyPropertyChanged, IDisposable
{
    private bool _done;
    private Bitmap? _icon;

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptModel"/> class and loads the application icon.
    /// </summary>
    /// <param name="title">The title of the dialog.</param>
    /// <param name="appId">The app id of the application that asks.</param>
    /// <param name="displayName">The display name of the application, or empty.</param>
    /// <param name="iconPath">The path of the icon file, or empty for no icon.</param>
    protected PromptModel(string title, string appId, string displayName, string iconPath = "")
    {
        Title = title;
        _icon = PromptIcon.Load(iconPath, IconSize);
        AppLine = string.IsNullOrEmpty(displayName)
            ? string.IsNullOrEmpty(appId) ? "An unknown application is asking" : $"{appId} is asking"
            : $"{displayName} ({appId}) is asking";
        Accept = new PromptCommand(() => Complete(PromptResponse.Accepted), () => CanAccept);
        Deny = new PromptCommand(() => Complete(PromptResponse.Denied), () => true);
        Cancel = new PromptCommand(() => Complete(PromptResponse.Cancelled), () => true);
    }

    /// <summary>
    /// Occurs when a bound property of the model changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Occurs once, when the prompt completes. The argument is the response.
    /// </summary>
    public event Action<PromptResponse>? Completed;

    /// <summary>
    /// Gets the title of the dialog.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// The size of the application icon in pixels.
    /// </summary>
    public const int IconSize = 32;

    /// <summary>
    /// Gets the line under the title that names the application that asks.
    /// </summary>
    public string AppLine { get; }

    /// <summary>
    /// Gets the application icon, or <see langword="null"/> when no icon loaded or after <see cref="Dispose"/>.
    /// </summary>
    public Bitmap? Icon => _icon;

    /// <summary>
    /// Gets a value indicating whether an icon loaded.
    /// </summary>
    public bool HasIcon => _icon is not null;

    /// <summary>
    /// Gets the label of the accept button. The default is <c>Allow</c>.
    /// </summary>
    public virtual string AcceptLabel => "Allow";

    /// <summary>
    /// Gets the label of the deny button. The default is <c>Deny</c>.
    /// </summary>
    public virtual string DenyLabel => "Deny";

    /// <summary>
    /// Gets the command that completes the prompt with an accepted response. It can run only while <see cref="CanAccept"/> is <see langword="true"/>.
    /// </summary>
    public ICommand Accept { get; }

    /// <summary>
    /// Gets the command that completes the prompt with a denied response.
    /// </summary>
    public ICommand Deny { get; }

    /// <summary>
    /// Gets the command that completes the prompt with a cancelled response.
    /// </summary>
    public ICommand Cancel { get; }

    /// <summary>
    /// Gets a value indicating whether the prompt completed.
    /// </summary>
    public bool IsDone => _done;

    /// <summary>
    /// Gets a value indicating whether the accept command is allowed. The default is always <see langword="true"/>.
    /// </summary>
    protected virtual bool CanAccept => true;

    /// <summary>
    /// Completes the prompt and raises <see cref="Completed"/>. A second call does nothing.
    /// </summary>
    /// <param name="response">The response to report.</param>
    public void Complete(PromptResponse response)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Completed?.Invoke(response);
    }

    /// <summary>
    /// Releases the icon bitmap.
    /// </summary>
    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }

    /// <summary>
    /// Raises <see cref="PropertyChanged"/> for a property and asks the view to check the accept command again.
    /// </summary>
    /// <param name="name">The name of the property. The caller name is the default.</param>
    protected void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ((PromptCommand)Accept).Refresh();
    }

    private sealed class PromptCommand(Action run, Func<bool> can) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => can();

        public void Execute(object? parameter)
        {
            if (can())
            {
                run();
            }
        }

        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
