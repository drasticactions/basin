using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabWm.Chrome;
using Xunit;

namespace TabWm.Tests;

public sealed class SettingsViewTests
{
    [AvaloniaFact]
    public void A_control_reports_its_change_and_loading_the_model_reports_nothing()
    {
        var changes = new List<(string Name, object Value)>();
        var model = new SettingsViewModel { ColorSchemes = ["Session default", "BreezeDark"] };
        model.Changed = (name, value) => changes.Add((name, value));
        model.Load(() =>
        {
            model.Variant = 2;
            model.CloseWhenEmpty = false;
            model.Terminal = "foot";
        });
        Assert.Empty(changes);

        var view = new SettingsView { DataContext = model };
        _ = ViewHarness.Show(view, 760, 620);
        ViewHarness.Pump();
        var check = view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "CloseWhenEmpty");
        check.IsChecked = true;
        var variant = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "Variant");
        Assert.Equal(2, variant.SelectedIndex);
        variant.SelectedIndex = 1;
        ViewHarness.Pump();

        Assert.Equal([(nameof(SettingsViewModel.CloseWhenEmpty), (object)true), (nameof(SettingsViewModel.Variant), 1)], changes);
    }

    [AvaloniaFact]
    public void The_close_button_dismisses_the_page()
    {
        var dismissed = false;
        var model = new SettingsViewModel { Dismiss = () => dismissed = true };
        var view = new SettingsView { DataContext = model };
        _ = ViewHarness.Show(view, 760, 620);
        ViewHarness.Pump();
        view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton").Command!.Execute(null);
        Assert.True(dismissed);
    }
}
