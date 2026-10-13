using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace BasinGreeter.UI;

public sealed partial class LoginView : UserControl
{
    private LoginModel? _model;

    public LoginView()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ListBox>("UserList")!.DoubleTapped += (_, _) => _model?.ChooseSelected.Execute(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.FocusRequested -= OnFocusRequested;
        }

        if (_model is not null)
        {
            _model.Bar.PropertyChanged -= OnBarChanged;
        }

        _model = DataContext as LoginModel;
        if (_model is not null)
        {
            _model.FocusRequested += OnFocusRequested;
            _model.Bar.PropertyChanged += OnBarChanged;
            _model.Bar.Network.FocusPassphrase += () => Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("Passphrase")?.Focus(NavigationMethod.Tab), DispatcherPriority.Input);
            ShowNetworkGlyph();
        }
    }

    private void OnBarChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BarModel.NetworkGlyph))
        {
            ShowNetworkGlyph();
        }
    }

    private void ShowNetworkGlyph()
    {
        if (_model is not null && this.TryFindResource(_model.Bar.NetworkGlyph, out var glyph) && glyph is Avalonia.Media.Geometry geometry)
        {
            this.FindControl<PathIcon>("NetworkGlyph")!.Data = geometry;
        }
    }

    private void OnFocusRequested() => Dispatcher.UIThread.Post(FocusCurrent, DispatcherPriority.ContextIdle);

    public Basin.SoftKeyboard.Avalonia.SoftKeyboardCanvas KeyboardCanvas => this.FindControl<Basin.SoftKeyboard.Avalonia.SoftKeyboardCanvas>("Keyboard")!;

    internal void FocusCurrent()
    {
        if (_model is null)
        {
            return;
        }

        Control? target = _model.ShowList
            ? this.FindControl<ListBox>("UserList")
            : _model.AskUsername ? this.FindControl<TextBox>("UsernameBox") : this.FindControl<TextBox>("Field");
        if (target is ListBox list && list.SelectedIndex >= 0 && list.ContainerFromIndex(list.SelectedIndex) is { } item)
        {
            item.Focus(NavigationMethod.Tab);
            return;
        }

        target?.Focus(NavigationMethod.Tab);
    }
}
