using System.Collections.ObjectModel;
using System.ComponentModel;
namespace Westonia.Shell;

public sealed class SwitcherEntry : INotifyPropertyChanged
{
    private bool _selected;

    public SwitcherEntry(string title) => Title = title;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }
}
