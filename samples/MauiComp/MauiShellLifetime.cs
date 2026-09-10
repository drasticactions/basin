using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace MauiComp;

internal sealed class MauiShellLifetime : ISingleViewApplicationLifetime
{
    public Control? MainView { get; set; }
}
