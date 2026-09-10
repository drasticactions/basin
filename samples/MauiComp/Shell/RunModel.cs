using System.ComponentModel;
using System.Windows.Input;

namespace MauiComp.Shell;

public sealed class RunModel : INotifyPropertyChanged
{
    private string _command = string.Empty;

    public RunModel(Action<string> run, Action cancel)
    {
        Ok = new RelayCommand(() => run(TextSource?.Invoke() ?? Command));
        Cancel = new RelayCommand(cancel);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand Ok { get; }

    public ICommand Cancel { get; }

    public Func<string?>? TextSource { get; set; }

    public string Command
    {
        get => _command;
        set
        {
            if (_command == value)
            {
                return;
            }

            _command = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Command)));
        }
    }
}
