using System.ComponentModel;
using System.Windows.Input;

namespace MauiComp.Shell;

public sealed class TaskEntry : INotifyPropertyChanged
{
    private string _title;
    private bool _active;

    public TaskEntry(string title, Action activate)
    {
        _title = title;
        Activate = new RelayCommand(activate);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand Activate { get; }

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
