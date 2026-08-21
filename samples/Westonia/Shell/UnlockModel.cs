using System.Windows.Input;

namespace Westonia.Shell;

public sealed class UnlockModel
{
    public UnlockModel(Action unlock) => Unlock = new RelayCommand(unlock);

    public string Hint { get; set; } = "Press Enter or Escape, or click Unlock";

    public ICommand Unlock { get; }
}
