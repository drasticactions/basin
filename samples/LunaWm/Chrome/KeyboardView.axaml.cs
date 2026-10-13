using Avalonia.Controls;
using Basin.SoftKeyboard.Avalonia;

namespace LunaWm;

public sealed partial class KeyboardView : UserControl
{
    private KeyboardModel? _model;

    public KeyboardView()
    {
        InitializeComponent();
    }

    public KeyboardModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.Changed -= OnChanged;
            }

            _model = value;
            Bar.Model = value;
            if (value is null)
            {
                Canvas.Engine = null;
                Canvas.Skin = null;
                Bar.Skin = null;
                return;
            }

            var skin = new LunaKeyboardSkin(value);
            Bar.Skin = skin;
            Canvas.Skin = skin;
            Canvas.Engine = value.Engine;
            value.Changed += OnChanged;
            OnChanged();
        }
    }

    internal SoftKeyboardCanvas Keys => Canvas;

    private void OnChanged()
    {
        if (_model is { } model)
        {
            Canvas.KeymapTop = model.KeymapTop;
        }

        Canvas.InvalidateVisual();
        Bar.InvalidateVisual();
    }
}
