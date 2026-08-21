using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace Westonia.Shell;

public sealed class PanelModel : INotifyPropertyChanged
{
    private string _clock = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LauncherModel> Launchers { get; } = [];

    public IBrush PanelBrush { get; set; } = Brushes.Black;

    public ThemeVariant Variant { get; set; } = ThemeVariant.Dark;

    public Dock ClockDock { get; set; } = Dock.Right;

    public bool ClockVisible { get; set; } = true;

    public Orientation Orientation { get; set; } = Orientation.Horizontal;

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
