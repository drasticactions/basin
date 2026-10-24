using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TabWm.Chrome;

public sealed partial class LauncherView : UserControl
{
    private readonly TextBox _search;
    private readonly ListBox _pinned;
    private readonly ListBox _results;
    private LauncherViewModel? _bound;
    private bool _focusWanted;
    private LauncherEntryModel? _dragged;
    private Point _pressPoint;
    private bool _dragging;

    public LauncherView()
    {
        AvaloniaXamlLoader.Load(this);
        _search = this.FindControl<TextBox>("Search")!;
        _pinned = this.FindControl<ListBox>("Pinned")!;
        _results = this.FindControl<ListBox>("Results")!;
        _results.SelectionChanged += (_, _) =>
        {
            if (Model is { } model && _results.SelectedIndex >= 0 && _results.SelectedIndex != model.SelectedIndex)
            {
                model.SelectedIndex = _results.SelectedIndex;
            }
        };
        _results.ContainerPrepared += (_, _) => Dispatcher.Post(SyncSelection);
        DataContextChanged += (_, _) => Bind();
        AttachedToVisualTree += (_, _) =>
        {
            if (_focusWanted)
            {
                Dispatcher.Post(FocusSearch);
            }
        };
        _search.AddHandler(KeyDownEvent, OnSearchKey, RoutingStrategies.Tunnel);
        AddHandler(TappedEvent, OnTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Bubble);
        _pinned.AddHandler(PointerPressedEvent, OnPinnedPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _pinned.AddHandler(PointerMovedEvent, OnPinnedMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _pinned.AddHandler(PointerReleasedEvent, OnPinnedReleased, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private LauncherViewModel? Model => DataContext as LauncherViewModel;

    public void FocusSearch()
    {
        _focusWanted = !_search.Focus();
        _search.SelectAll();
    }

    private void Bind()
    {
        if (_bound is { } old)
        {
            old.PropertyChanged -= OnModelChanged;
        }

        _bound = Model;
        if (_bound is { } model)
        {
            model.PropertyChanged += OnModelChanged;
        }

        SyncSelection();
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherViewModel.SelectedIndex))
        {
            SyncSelection();
        }
    }

    private void SyncSelection()
    {
        if (Model is { } model && _results.SelectedIndex != model.SelectedIndex && model.SelectedIndex < _results.ItemCount)
        {
            _results.SelectedIndex = model.SelectedIndex;
        }
    }

    private void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                model.Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                model.Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                model.Enter();
                e.Handled = true;
                break;
            case Key.Escape:
                if (model.Searching)
                {
                    model.Reset();
                }
                else
                {
                    model.Dismiss?.Invoke();
                }

                e.Handled = true;
                break;
        }
    }

    private static LauncherEntryModel? EntryOf(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: LauncherEntryModel entry } ? entry : null;

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_dragging || EntryOf(e.Source) is not { } entry || Model is not { } model)
        {
            return;
        }

        model.LaunchCommand.Execute(entry);
        e.Handled = true;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (EntryOf(e.Source) is not { IsRun: false } entry || Model is not { } model || e.Source is not Visual visual ||
            visual.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { } item)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = entry.IsPinned ? "Unpin" : "Pin", Command = model.PinCommand, CommandParameter = entry });
        item.ContextMenu = menu;
        menu.Open(item);
        e.Handled = true;
    }

    private void OnPinnedPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = false;
        _dragged = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? EntryOf(e.Source) : null;
        _pressPoint = e.GetPosition(_pinned);
    }

    private void OnPinnedMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is not { } entry || Model is not { } model)
        {
            return;
        }

        var point = e.GetPosition(_pinned);
        if (!_dragging && Math.Abs(point.X - _pressPoint.X) < 4 && Math.Abs(point.Y - _pressPoint.Y) < 4)
        {
            return;
        }

        _dragging = true;
        for (var i = 0; i < model.Pinned.Count; i++)
        {
            if (_pinned.ContainerFromIndex(i) is Control container && container.TranslatePoint(default, _pinned) is { } origin &&
                new Rect(origin, container.Bounds.Size).Contains(point) && !ReferenceEquals(model.Pinned[i], entry))
            {
                model.MovePinned?.Invoke(entry.Id, i);
                break;
            }
        }
    }

    private void OnPinnedReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragged = null;
        if (_dragging)
        {
            e.Handled = true;
            Dispatcher.Post(() => _dragging = false);
        }
    }
}
