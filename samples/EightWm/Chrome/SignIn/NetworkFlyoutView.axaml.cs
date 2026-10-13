using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace EightWm;

public sealed partial class NetworkFlyoutView : UserControl
{
    private NetworkModel? _model;

    public NetworkFlyoutView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.FocusPassphrase -= OnFocusPassphrase;
        }

        _model = DataContext as NetworkModel;
        if (_model is not null)
        {
            _model.FocusPassphrase += OnFocusPassphrase;
        }
    }

    private void OnFocusPassphrase() =>
        Dispatcher.UIThread.Post(() => Passphrase.Focus(NavigationMethod.Tab), DispatcherPriority.Input);
}
