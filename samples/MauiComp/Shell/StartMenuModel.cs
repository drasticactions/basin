using System.Windows.Input;

namespace MauiComp.Shell;

public sealed class StartMenuModel
{
    public StartMenuModel(Action programs, Action run, Action exit)
    {
        Programs = new RelayCommand(programs);
        Run = new RelayCommand(run);
        Exit = new RelayCommand(exit);
    }

    public ICommand Programs { get; }

    public ICommand Run { get; }

    public ICommand Exit { get; }
}
