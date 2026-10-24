using System.Collections.ObjectModel;
using System.Windows.Input;

namespace TabWm.Chrome;

public sealed class BannerModel : ObservableModel
{
    private string _message = string.Empty;

    public BannerModel(Action<ulong> focus, Action keep)
    {
        FocusCommand = new RelayCommand(window =>
        {
            if (window is HiddenWindowModel entry)
            {
                focus(entry.Id);
            }
        });
        KeepCommand = new RelayCommand(keep);
    }

    public string Message
    {
        get => _message;
        set => Set(ref _message, value);
    }

    public ObservableCollection<HiddenWindowModel> Windows { get; } = [];

    public ICommand FocusCommand { get; }

    public ICommand KeepCommand { get; }
}
