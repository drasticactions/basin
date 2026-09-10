using System.Windows.Input;

namespace MauiComp.Shell;

public sealed class RelayCommand : ICommand
{
    private readonly Action _run;

    public RelayCommand(Action run)
    {
        _run = run;
    }

    public event EventHandler? CanExecuteChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _run();
}
