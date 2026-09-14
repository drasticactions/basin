using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

public abstract class PromptModel : INotifyPropertyChanged, IDisposable
{
    private bool _done;
    private Bitmap? _icon;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<PromptResponse>? Completed;

    public string Title { get; }

    public const int IconSize = 32;

    public string AppLine { get; }

    public Bitmap? Icon => _icon;

    public bool HasIcon => _icon is not null;

    public virtual string AcceptLabel => "Allow";

    public virtual string DenyLabel => "Deny";

    public ICommand Accept { get; }

    public ICommand Deny { get; }

    public ICommand Cancel { get; }

    public bool IsDone => _done;

    protected virtual bool CanAccept => true;

    public void Complete(PromptResponse response)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Completed?.Invoke(response);
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }

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
