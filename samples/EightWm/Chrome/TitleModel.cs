using System.Windows.Input;

namespace EightWm;

public sealed class TitleModel(Action close) : ObservableModel
{
    private string _title = string.Empty;

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public ICommand CloseCommand { get; } = new RelayCommand(_ => close());
}
