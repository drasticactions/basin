using System.Windows.Input;
using Avalonia.Media;

namespace EightWm;

public sealed class CharmsModel(Action<string> activate) : ObservableModel
{
    private string _clock = string.Empty;
    private string _date = string.Empty;
    private byte _fillAlpha = 0xf0;

    public ICommand ActivateCommand { get; } = new RelayCommand(name => activate(name as string ?? string.Empty));

    public string Clock
    {
        get => _clock;
        set => Set(ref _clock, value);
    }

    public string Date
    {
        get => _date;
        set => Set(ref _date, value);
    }

    public byte FillAlpha
    {
        get => _fillAlpha;
        set
        {
            if (Set(ref _fillAlpha, value))
            {
                Raise(nameof(BarFill));
            }
        }
    }

    public IBrush BarFill => new SolidColorBrush(Color.FromArgb(_fillAlpha, 0x1f, 0x1f, 0x1f));
}
