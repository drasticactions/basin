using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MauiComp.Shell;

public sealed class PanelModel : INotifyPropertyChanged
{
    private string _clock = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TaskEntry> Tasks { get; } = [];

    public Action? StartRequested { get; set; }

    public string Clock
    {
        get => _clock;
        set
        {
            if (_clock == value)
            {
                return;
            }

            _clock = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Clock)));
        }
    }
}
