using System.Collections.ObjectModel;
using System.Windows.Input;

namespace TabWm.Chrome;

public sealed class LauncherViewModel : ObservableModel
{
    private string _search = string.Empty;
    private int _selected = -1;
    private bool _overlay;

    public LauncherViewModel()
    {
        LaunchCommand = new RelayCommand(entry =>
        {
            if (entry is LauncherEntryModel model)
            {
                Activate(model);
            }
        });
        PinCommand = new RelayCommand(entry =>
        {
            if (entry is LauncherEntryModel model)
            {
                Pin?.Invoke(model.Id, !model.IsPinned);
            }
        });
    }

    public ObservableCollection<LauncherEntryModel> Pinned { get; } = [];

    public ObservableCollection<LauncherCategoryModel> Categories { get; } = [];

    public ObservableCollection<LauncherEntryModel> Results { get; } = [];

    public bool IsOverlay
    {
        get => _overlay;
        set => Set(ref _overlay, value);
    }

    public string SearchText
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
            {
                Raise(nameof(Searching));
                Searched?.Invoke(value);
            }
        }
    }

    public bool Searching => _search.Trim().Length > 0;

    public bool HasPinned => Pinned.Count > 0;

    public int SelectedIndex
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public ICommand LaunchCommand { get; }

    public ICommand PinCommand { get; }

    public Action<string>? Searched { get; set; }

    public Action<LauncherEntryModel>? Launch { get; set; }

    public Action<string>? Run { get; set; }

    public Action<string, bool>? Pin { get; set; }

    public Action<string, int>? MovePinned { get; set; }

    public Action? Dismiss { get; set; }

    public void SetResults(IEnumerable<LauncherEntryModel> results)
    {
        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(result);
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    public void SetPinned(IEnumerable<LauncherEntryModel> pinned)
    {
        Pinned.Clear();
        foreach (var entry in pinned)
        {
            entry.IsPinned = true;
            Pinned.Add(entry);
        }

        Raise(nameof(HasPinned));
    }

    public void Move(int step)
    {
        if (Results.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Clamp(SelectedIndex + step, 0, Results.Count - 1);
    }

    public void Enter()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            Activate(Results[SelectedIndex]);
        }
    }

    public void Reset()
    {
        SearchText = string.Empty;
        Results.Clear();
        SelectedIndex = -1;
    }

    private void Activate(LauncherEntryModel model)
    {
        if (model.IsRun)
        {
            Run?.Invoke(SearchText.Trim());
            return;
        }

        Launch?.Invoke(model);
    }
}
