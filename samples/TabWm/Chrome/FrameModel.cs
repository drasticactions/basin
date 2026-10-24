using System.Windows.Input;

namespace TabWm.Chrome;

public sealed class FrameModel : ObservableModel
{
    private string _title = string.Empty;
    private bool _active = true;
    private bool _maximized;

    public FrameModel(Action minimize, Action maximize, Action close)
    {
        MinimizeCommand = new RelayCommand(minimize);
        MaximizeCommand = new RelayCommand(maximize);
        CloseCommand = new RelayCommand(close);
    }

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public bool Active
    {
        get => _active;
        set
        {
            if (Set(ref _active, value))
            {
                Raise(nameof(Hints));
            }
        }
    }

    public string? Hints => _active ? null : "inactive";

    public bool Maximized
    {
        get => _maximized;
        set
        {
            if (Set(ref _maximized, value))
            {
                Raise(nameof(MaximizeHints));
            }
        }
    }

    public string? MaximizeHints => _maximized ? "maximized" : null;

    public ICommand MinimizeCommand { get; }

    public ICommand MaximizeCommand { get; }

    public ICommand CloseCommand { get; }
}
