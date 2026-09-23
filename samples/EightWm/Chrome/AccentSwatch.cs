using System.Windows.Input;
using Avalonia.Media;

namespace EightWm;

public sealed class AccentSwatch : ObservableModel
{
    private bool _isCurrent;

    public AccentSwatch(uint argb, Action<uint> choose)
    {
        Argb = argb;
        Fill = new SolidColorBrush(Color.FromUInt32(argb));
        ChooseCommand = new RelayCommand(_ => choose(argb));
    }

    public uint Argb { get; }

    public IBrush Fill { get; }

    public ICommand ChooseCommand { get; }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => Set(ref _isCurrent, value);
    }
}
