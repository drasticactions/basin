using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Basin.Capabilities;
using Basin.UI.Avalonia;
using Xunit;

namespace Basin.Tests;

public sealed class AvaloniaAccessibilityTests
{
    private sealed class OneScreen : IUIScreenSource
    {
        public int Count => 1;

        public bool TryGet(int index, out UIScreenInfo info)
        {
            info = new UIScreenInfo("shell", 0, 0, 1280, 720, 1.0, true);
            return index == 0;
        }

        public event Action? Changed
        {
            add
            {
            }

            remove
            {
            }
        }
    }

    private static async Task Settle(int rounds = 5)
    {
        for (var i = 0; i < rounds; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<T> While<T>(Task<T> work)
    {
        while (!work.IsCompleted)
        {
            await Settle(1);
        }

        return await work;
    }

    private static async Task While(Task work)
    {
        while (!work.IsCompleted)
        {
            await Settle(1);
        }

        await work;
    }

    private static AvaloniaUISurface Surface(AvaloniaUIHost host, int width, int height) =>
        (AvaloniaUISurface)host.CreateSurface(new UISurfaceOptions { Target = UITargetKind.Memory, Width = width, Height = height, Scale = 1.0 })!;

    [AvaloniaFact]
    public async Task Two_surfaces_are_two_windows_and_a_named_button_has_its_layout_box_and_presses()
    {
        var bus = await AccessibilityBus.ForProcessAsync();
        var owner = Basin.Diagnostics.ThreadAffinity.Capture();
        using var host = BasinPlatform.Attach(new BasinPlatformOptions { Screens = new OneScreen(), CompositorAffinity = owner });
        var strip = Surface(host, 400, 60);
        strip.AccessibleName = "Strip";
        var clicks = 0;
        var add = new Button { Content = "+", Width = 40, Height = 30, Margin = new global::Avalonia.Thickness(10, 15, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(add, "New tab");
        add.Click += (_, _) => clicks++;
        strip.Content = new Grid { Children = { add } };
        var panel = Surface(host, 300, 200);
        panel.AccessibleName = "Panel";
        panel.Content = new TextBlock { Text = "Settings" };
        strip.SetPosition(100, 50);
        panel.SetPosition(600, 300);

        Assert.Null(await host.StartAccessibilityAsync());
        Assert.True(host.IsAccessible);
        await Settle();

        using var reader = await While(bus.ConnectAsync());
        var nodes = await While(reader.WhereAsync(found => found.Any(n => n.Name == "New tab") && found.Any(n => n.Name == "Panel")));
        Assert.Contains(nodes, n => n.Name == "Strip" && n.Role is "frame" or "window");
        Assert.Contains(nodes, n => n.Name == "Panel" && n.Role is "frame" or "window");
        var button = nodes.First(n => n.Name == "New tab");
        Assert.Equal("push button", button.Role);

        var extents = await While(reader.ExtentsAsync(button));
        Assert.Equal(new AtSpiRect(110, 65, 40, 30), extents);

        Assert.True(await While(reader.DoActionAsync(button, "click")));
        for (var i = 0; i < 50 && clicks == 0; i++)
        {
            await Settle(1);
        }

        Assert.Equal(1, clicks);

        host.StopAccessibility();
        Assert.False(host.IsAccessible);
        await Settle();
        await While(reader.WhereAsync(found => !found.Any(n => n.Name == "New tab")));
    }

    [AvaloniaFact]
    public async Task A_new_subtree_whose_text_changes_in_the_same_frame_reads_its_text_and_caret()
    {
        var bus = await AccessibilityBus.ForProcessAsync();
        var owner = Basin.Diagnostics.ThreadAffinity.Capture();
        using var host = BasinPlatform.Attach(new BasinPlatformOptions { Screens = new OneScreen(), CompositorAffinity = owner });
        var surface = Surface(host, 400, 200);
        surface.AccessibleName = "Form";
        surface.Content = new TextBlock { Text = "Loading" };
        Assert.Null(await host.StartAccessibilityAsync());
        await Settle();
        using var reader = await While(bus.ConnectAsync());
        await While(reader.FindAsync("Loading"));

        var field = new TextBox { Width = 200, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(field, "Title");
        surface.Content = new StackPanel { Children = { new TextBlock { Text = "Edit" }, field } };
        field.Text = "first";
        field.Focus();
        field.Text = "Hello world";
        field.CaretIndex = 5;
        await Settle();

        var node = await While(reader.FindAsync("Title", "entry"));
        string? text = null;
        for (var i = 0; i < 50 && text != "Hello world"; i++)
        {
            text = await While(reader.TextAsync(node));
            await Settle(1);
        }

        Assert.Equal("Hello world", text);
        Assert.Equal(5, await While(reader.CaretOffsetAsync(node)));
        Assert.False(await While(reader.HasStateAsync(node, AtSpiState.Focused)));

        surface.NotifyKeyboardEnter([]);
        await Settle();
        await While(reader.WaitForStateAsync(node, AtSpiState.Focused));
        surface.NotifyKeyboardLeave();
        await Settle();
        await While(reader.WaitForStateAsync(node, AtSpiState.Focused, value: false));
    }

    [AvaloniaFact]
    public async Task A_control_renamed_and_moved_into_a_new_subtree_in_one_frame_reads_once_under_its_new_name()
    {
        var bus = await AccessibilityBus.ForProcessAsync();
        var owner = Basin.Diagnostics.ThreadAffinity.Capture();
        using var host = BasinPlatform.Attach(new BasinPlatformOptions { Screens = new OneScreen(), CompositorAffinity = owner });
        var surface = Surface(host, 400, 200);
        surface.AccessibleName = "Mover";
        var moving = new Button { Content = "Go" };
        AutomationProperties.SetName(moving, "Before");
        var first = new StackPanel { Children = { moving } };
        var second = new StackPanel();
        surface.Content = new StackPanel { Children = { first, second } };
        Assert.Null(await host.StartAccessibilityAsync());
        await Settle();
        using var reader = await While(bus.ConnectAsync());
        await While(reader.FindAsync("Before", "push button"));

        AutomationProperties.SetName(moving, "After");
        first.Children.Remove(moving);
        second.Children.Add(new Border { Child = new StackPanel { Children = { new Border { Child = moving } } } });
        await Settle();

        var nodes = await While(reader.WhereAsync(found => found.Any(n => n.Name == "After")));
        Assert.Single(nodes, n => n.Role == "push button");
        Assert.DoesNotContain(nodes, n => n.Name == "Before");
    }

    [AvaloniaFact]
    public async Task A_panel_whose_children_change_in_the_frame_its_parent_drops_it_leaves_the_tree()
    {
        var bus = await AccessibilityBus.ForProcessAsync();
        var owner = Basin.Diagnostics.ThreadAffinity.Capture();
        using var host = BasinPlatform.Attach(new BasinPlatformOptions { Screens = new OneScreen(), CompositorAffinity = owner });
        var surface = Surface(host, 400, 200);
        surface.AccessibleName = "Recycler";
        var inner = new StackPanel { Children = { new Button { Content = "Old" } } };
        var middle = new StackPanel { Children = { inner } };
        var outer = new StackPanel { Children = { middle, new Button { Content = "Stays" } } };
        surface.Content = outer;
        Assert.Null(await host.StartAccessibilityAsync());
        await Settle();
        using var reader = await While(bus.ConnectAsync());
        await While(reader.FindAsync("Old", "push button"));

        outer.Children.Remove(middle);
        inner.Children.Add(new Button { Content = "New" });
        await Settle();

        var nodes = await While(reader.WhereAsync(found => !found.Any(n => n.Name == "Old")));
        Assert.Contains(nodes, n => n.Name == "Stays");
        Assert.DoesNotContain(nodes, n => n.Name == "New");
    }

    [AvaloniaFact]
    public async Task A_control_that_moves_every_frame_sends_its_bounds_in_batches_and_ends_at_its_last_box()
    {
        var bus = await AccessibilityBus.ForProcessAsync();
        var owner = Basin.Diagnostics.ThreadAffinity.Capture();
        using var host = BasinPlatform.Attach(new BasinPlatformOptions { Screens = new OneScreen(), CompositorAffinity = owner });
        var surface = Surface(host, 400, 200);
        surface.AccessibleName = "Slider";
        var moving = new Button { Content = "Moving", Width = 40, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        surface.Content = new Grid { Children = { moving } };
        Assert.Null(await host.StartAccessibilityAsync());
        await Settle();
        using var reader = await While(bus.ConnectAsync());
        var node = await While(reader.FindAsync("Moving", "push button"));
        await While(reader.ListenAsync());

        const int frames = 30;
        for (var i = 1; i <= frames; i++)
        {
            moving.Margin = new global::Avalonia.Thickness(i * 5, 0, 0, 0);
            await Settle(1);
        }

        AtSpiRect extents = default;
        for (var i = 0; i < 50 && extents.X != frames * 5; i++)
        {
            await Settle(1);
            extents = await While(reader.ExtentsAsync(node, AtSpiCoordinates.Window));
        }

        Assert.Equal(new AtSpiRect(frames * 5, 0, 40, 30), extents);
        var moves = reader.Events().Count(e => e.Member == "BoundsChanged" && e.Path == node.Path);
        Assert.InRange(moves, 1, frames / 3);
    }

    [AvaloniaFact]
    public async Task A_combo_box_reads_its_shown_item_and_follows_a_change()
    {
        var bus = await AccessibilityBus.ForProcessAsync();
        var owner = Basin.Diagnostics.ThreadAffinity.Capture();
        using var host = BasinPlatform.Attach(new BasinPlatformOptions { Screens = new OneScreen(), CompositorAffinity = owner });
        var surface = Surface(host, 400, 200);
        surface.AccessibleName = "Choices";
        var combo = new ComboBox { ItemsSource = new[] { "Light", "Dark" }, SelectedIndex = 0 };
        AutomationProperties.SetName(combo, "Variant");
        surface.Content = combo;
        Assert.Null(await host.StartAccessibilityAsync());
        await Settle();
        using var reader = await While(bus.ConnectAsync());
        var node = await While(reader.FindAsync("Variant", "combo box"));
        Assert.Contains(await While(reader.ChildrenAsync(node)), n => n.Role == "list item" && n.Name == "Light");

        combo.SelectedIndex = 1;
        List<AtSpiNode> children = [];
        for (var i = 0; i < 50 && !children.Any(n => n.Name == "Dark"); i++)
        {
            await Settle(1);
            children = await While(reader.ChildrenAsync(node));
        }

        Assert.Contains(children, n => n.Role == "list item" && n.Name == "Dark");
        Assert.DoesNotContain(children, n => n.Name == "Light");
        Assert.True(await While(reader.HasStateAsync(children.Single(n => n.Role == "list item" && n.Name == "Dark"), AtSpiState.Selected)));
    }

    [AvaloniaFact]
    public async Task The_chrome_window_under_the_pointer_is_found_by_its_extents_with_a_window_relative_point()
    {
        var bus = await AccessibilityBus.ForProcessAsync();
        var owner = Basin.Diagnostics.ThreadAffinity.Capture();
        using var host = BasinPlatform.Attach(new BasinPlatformOptions { Screens = new OneScreen(), CompositorAffinity = owner });
        var left = Surface(host, 200, 100);
        left.AccessibleName = "Left";
        left.Content = new TextBlock { Text = "left" };
        left.SetPosition(0, 0);
        var right = Surface(host, 300, 120);
        right.AccessibleName = "Right";
        right.Content = new TextBlock { Text = "right" };
        right.SetPosition(500, 200);
        Assert.Null(await host.StartAccessibilityAsync());
        await Settle();
        using var reader = await While(bus.ConnectAsync());
        var window = await While(reader.FindAsync("Right", "frame"));

        using var locator = new Basin.Accessibility.ChromeLocator(bus.SessionAddress);
        var found = await While(locator.FindAsync(530, 260));
        Assert.NotNull(found);
        Assert.Equal(window.Bus, found.Value.AppBusName);
        Assert.Equal(window.Path, found.Value.ToplevelPath);
        Assert.Equal((30.0, 60.0), (found.Value.X, found.Value.Y));
        Assert.Null(await While(locator.FindAsync(1000, 700)));
    }
}

