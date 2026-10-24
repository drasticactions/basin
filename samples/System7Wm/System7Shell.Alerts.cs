using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Basin.Diagnostics;
using Basin.Session;
using System7.Avalonia;
using System7.Avalonia.Controls;
using System7.Avalonia.Rendering;

namespace System7Wm;

internal sealed partial class System7Shell
{
    private int _alertSerial;

    internal ShellWindow ShowAlert(string title, string message, string[] buttons, Action<int> chosen, string icon = "caution", int defaultButton = -1)
    {
        var width = 340;
        var lines = Math.Max(2, (int)Math.Ceiling(System7Font.Measure(message) / 250.0) + message.Count(c => c == '\n'));
        var height = Math.Max(110, 60 + (lines * 16) + 30);
        var window = default(ShellWindow);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, HorizontalAlignment = HorizontalAlignment.Right };
        var choice = defaultButton < 0 ? buttons.Length - 1 : defaultButton;
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 64, Padding = new Thickness(10, 0), Height = 20, IsDefault = i == choice, IsCancel = buttons.Length > 1 && i == 0 };
            button.Click += (_, _) =>
            {
                if (window is not null)
                {
                    CloseShellWindow(window);
                }

                BasinReport.Line($"ALERT {title} {buttons[index]}");
                chosen(index);
            };
            row.Children.Add(button);
        }

        var body = new Grid
        {
            Margin = new Thickness(13, 13, 13, 13),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("*,Auto"),
        };
        var picture = new System7IconView { Icon = System7IconFamily.Generic(icon), Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top };
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(23, 0, 0, 10) };
        Grid.SetColumn(text, 1);
        Grid.SetRow(row, 1);
        Grid.SetColumnSpan(row, 2);
        body.Children.Add(picture);
        body.Children.Add(text);
        body.Children.Add(row);
        System7Theme.SetUseNativeDialogKeys(body, true);
        window = OpenShellWindow($"alert-{++_alertSerial}", title, body, width, height, closable: false, kind: System7WindowKind.Dialog, alert: true);
        BasinReport.Line($"ALERT {title} shown");
        return window;
    }

    internal bool ChooseAlert(string label)
    {
        if (_stack.LastOrDefault(w => w.IsAlert && w.Mapped) is not { ShellFrame.Content: Control body })
        {
            return false;
        }

        foreach (var button in body.GetLogicalDescendants().OfType<Button>())
        {
            if (string.Equals(button.Content as string, label, StringComparison.OrdinalIgnoreCase))
            {
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                return true;
            }
        }

        return false;
    }

    private void AskEmptyTrash()
    {
        var summary = Trash.Summary();
        if (summary.Items == 0)
        {
            return;
        }

        var kilobytes = Math.Max(1, (summary.Bytes + 1023) / 1024);
        var items = summary.Items == 1 ? "1 item, which uses" : $"{summary.Items} items, which use";
        ShowAlert("Empty Trash", $"The Trash contains {items} {kilobytes:N0}K of disk space. Are you sure you want to permanently remove these items?",
            ["Cancel", "OK"], choice =>
            {
                if (choice == 1)
                {
                    EmptyTrash();
                }
            });
    }

    internal void EmptyTrash()
    {
        try
        {
            Trash.Empty();
            BasinReport.Line("TRASH emptied");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Warn(error, $"finder: cannot empty the Trash");
            ShowAlert("Empty Trash", FileErrorText("The Trash could not be emptied", error), ["OK"], _ => { }, icon: "stop");
        }

        RefreshTrash();
    }

    private void AskPower(LogindAction action)
    {
        var (verb, message) = action switch
        {
            LogindAction.Suspend => ("Sleep", "Are you sure you want to put the computer to sleep now?"),
            LogindAction.Reboot => ("Restart", "Are you sure you want to restart your computer now?"),
            _ => ("Shut Down", "Are you sure you want to shut down your computer now?"),
        };
        ShowAlert(verb, message, ["Cancel", verb], choice =>
        {
            if (choice == 1)
            {
                _ = LogindActions.RunAsync(action);
            }
        });
    }

    private void AskLogOut()
    {
        ShowAlert("Log Out", "Are you sure you want to quit all applications and log out now?", ["Cancel", "Log Out"], choice =>
        {
            if (choice == 1)
            {
                LogOut();
            }
        });
    }

    private void LogOut()
    {
        foreach (var window in _stack.Where(w => !w.IsShell).ToList())
        {
            window.Close();
        }

        var deadline = Environment.TickCount64 + 3000;
        Basin.IEventSource? timer = null;
        timer = _host.Loop.AddTimer(() =>
        {
            if (_stack.All(w => w.IsShell) || Environment.TickCount64 > deadline)
            {
                timer?.Remove();
                Stop();
                return;
            }

            timer?.UpdateTimer(100);
        });
        timer.UpdateTimer(100);
    }
}
