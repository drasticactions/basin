using System.Collections.ObjectModel;
using System.Windows.Input;

namespace TabWm.Chrome;

public sealed class StripModel : ObservableModel
{
    private int _selectedIndex = -1;
    private string _clock = string.Empty;
    private bool _anyAudio;
    private bool _syncing;
    private double _height;

    public StripModel()
    {
        NewTabCommand = new RelayCommand(() => NewTab?.Invoke());
        MenuCommand = new RelayCommand(name => MenuAction?.Invoke(name as string ?? string.Empty));
        RenameCommand = new RelayCommand(tab => BeginRename(tab as TabModel));
        CloseCommand = new RelayCommand(tab =>
        {
            if (tab is TabModel model)
            {
                CloseRequested?.Invoke(model);
            }
        });
        MuteCommand = new RelayCommand(tab =>
        {
            if (tab is TabModel model)
            {
                MuteToggled?.Invoke(model);
            }
        });
        LaunchCommand = new RelayCommand(tab =>
        {
            if (tab is TabModel model)
            {
                LaunchInto?.Invoke(model);
            }
        });
        RestoreCommand = new RelayCommand(window =>
        {
            if (window is HiddenWindowModel hidden)
            {
                RestoreWindow?.Invoke(hidden.Id);
            }
        });
    }

    public ObservableCollection<TabModel> Tabs { get; } = [];

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (Set(ref _selectedIndex, value) && !_syncing && value >= 0 && value < Tabs.Count)
            {
                Selected?.Invoke(Tabs[value]);
            }
        }
    }

    public string Clock
    {
        get => _clock;
        set => Set(ref _clock, value);
    }

    public bool AnyAudio
    {
        get => _anyAudio;
        set => Set(ref _anyAudio, value);
    }

    public double PreferredHeight
    {
        get => _height;
        set
        {
            if (Set(ref _height, value))
            {
                HeightChanged?.Invoke(value);
            }
        }
    }

    public ICommand NewTabCommand { get; }

    public ICommand MenuCommand { get; }

    public ICommand RenameCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand MuteCommand { get; }

    public ICommand LaunchCommand { get; }

    public ICommand RestoreCommand { get; }

    public Action<TabModel>? Selected { get; set; }

    public Action<TabModel>? CloseRequested { get; set; }

    public Action? NewTab { get; set; }

    public Action<TabModel, int>? Reordered { get; set; }

    public Action<TabModel, string?>? Renamed { get; set; }

    public Action<TabModel>? MuteToggled { get; set; }

    public Action<TabModel>? LaunchInto { get; set; }

    public Action<ulong>? RestoreWindow { get; set; }

    public Action<string>? MenuAction { get; set; }

    public Func<TabModel, bool>? DragOut { get; set; }

    public Action<double>? HeightChanged { get; set; }

    public Action<TabModel>? RenameStarted { get; set; }

    public void Select(int index)
    {
        _syncing = true;
        try
        {
            SelectedIndex = index;
        }
        finally
        {
            _syncing = false;
        }
    }

    public void Move(TabModel tab, int index)
    {
        var from = Tabs.IndexOf(tab);
        if (from < 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, Tabs.Count - 1);
        if (from == index)
        {
            return;
        }

        var selected = SelectedIndex >= 0 && SelectedIndex < Tabs.Count ? Tabs[SelectedIndex] : null;
        _syncing = true;
        try
        {
            Tabs.RemoveAt(from);
            Tabs.Insert(index, tab);
            if (selected is not null)
            {
                SelectedIndex = Tabs.IndexOf(selected);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    public void BeginRename(TabModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        tab.EditText = tab.Label;
        RenameStarted?.Invoke(tab);
        tab.IsEditing = true;
    }

    public void CommitRename(TabModel tab, bool keep)
    {
        if (!tab.IsEditing)
        {
            return;
        }

        tab.IsEditing = false;
        var text = keep ? tab.EditText.Trim() : string.Empty;
        Renamed?.Invoke(tab, text.Length == 0 ? null : text);
    }

    public void Insert(int index, TabModel tab)
    {
        _syncing = true;
        try
        {
            Tabs.Insert(Math.Clamp(index, 0, Tabs.Count), tab);
        }
        finally
        {
            _syncing = false;
        }
    }

    public void Remove(TabModel tab)
    {
        _syncing = true;
        try
        {
            Tabs.Remove(tab);
        }
        finally
        {
            _syncing = false;
        }
    }
}
