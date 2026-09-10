using System.ComponentModel;
using System.Windows.Input;

namespace MauiComp.Shell;

public sealed class TitlebarModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private bool _active;

    public TitlebarModel(Action close, Action maximize, Action minimize)
    {
        Close = new RelayCommand(close);
        Maximize = new RelayCommand(maximize);
        Minimize = new RelayCommand(minimize);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand Close { get; }

    public ICommand Maximize { get; }

    public ICommand Minimize { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value)
            {
                return;
            }

            _title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }

            _active = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Active)));
        }
    }
}
