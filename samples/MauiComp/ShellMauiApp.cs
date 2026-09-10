using MauiComp.Shell;
using Microsoft.Maui;
using M = Microsoft.Maui.Controls;

namespace MauiComp;

public sealed class ShellMauiApp : M.Application
{
    public ShellMauiApp()
    {
        Resources.MergedDictionaries.Add(new RoyaleTheme());
    }

    internal M.Page? Pending { get; set; }

    protected override M.Window CreateWindow(IActivationState? activationState)
    {
        var page = Pending ?? new M.ContentPage();
        Pending = null;
        return new M.Window(page);
    }
}
