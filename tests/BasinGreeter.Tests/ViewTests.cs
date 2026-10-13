using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using BasinGreeter.UI;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class ViewTests
{
    private static (Window Window, LoginView View, LoginModel Model) Show()
    {
        var model = new LoginModel();
        model.Users.Add(new UserEntry("ada", "Ada Lovelace", null));
        model.Users.Add(new UserEntry("charles", "Charles Babbage", null));
        model.Users.Add(new UserEntry("", "Other…", null) { IsOther = true });
        model.Sessions.Add(new SessionEntry("sway", "Sway"));
        model.Sessions.Add(new SessionEntry("i3", "i3"));
        model.SelectedSession = model.Sessions[0];
        var view = new LoginView { DataContext = model };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        window.Show();
        return (window, view, model);
    }

    private static T Named<T>(LoginView view, string name)
        where T : Control => view.FindControl<T>(name)!;

    [AvaloniaFact]
    public void The_user_list_shows_every_user_and_other_with_initials()
    {
        var (window, view, model) = Show();
        model.ShowUsers(null);
        var list = Named<ListBox>(view, "UserList");
        Assert.True(list.IsEffectivelyVisible);
        Assert.Equal(3, list.ItemCount);
        Assert.Same(model.Users[0], list.SelectedItem);
        Assert.Equal("AL", model.Users[0].Initials);
        Assert.Equal("CB", model.Users[1].Initials);
        Assert.False(Named<TextBox>(view, "Field").IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Choosing_a_user_raises_the_choice_and_other_asks_for_a_name()
    {
        var (window, view, model) = Show();
        model.ShowUsers(null);
        UserEntry? chosen = null;
        model.UserChosen += user => chosen = user;
        model.SelectedUser = model.Users[1];
        model.Submit.Execute(null);
        Assert.Same(model.Users[1], chosen);

        model.AskForUsername();
        Assert.True(Named<TextBox>(view, "UsernameBox").IsEffectivelyVisible);
        string? entered = null;
        model.UsernameEntered += name => entered = name;
        model.UsernameText = "  grace ";
        model.Submit.Execute(null);
        Assert.Equal("grace", entered);
        window.Close();
    }

    [AvaloniaFact]
    public void A_secret_prompt_masks_and_a_visible_prompt_does_not()
    {
        var (window, view, model) = Show();
        model.BeginConversation("Ada Lovelace");
        model.ShowPrompt("Password: ", secret: true);
        var field = Named<TextBox>(view, "Field");
        Assert.True(field.IsEffectivelyVisible);
        Assert.Equal('●', field.PasswordChar);
        Assert.Equal("Password:", model.PromptText);

        string? answered = null;
        model.Answered += text => answered = text;
        model.FieldText = "hunter2";
        model.Submit.Execute(null);
        Assert.Equal("hunter2", answered);
        Assert.Equal("", model.FieldText);
        Assert.False(model.CanSubmit);

        model.ShowPrompt("Verification code: ", secret: false);
        Assert.Equal('\0', field.PasswordChar);
        window.Close();
    }

    [AvaloniaFact]
    public void Info_lines_pile_up_and_an_error_needs_dismissing()
    {
        var (window, view, model) = Show();
        model.BeginConversation("Ada Lovelace");
        model.AddMessage("Touch the key");
        model.AddMessage("Still waiting");
        Assert.Equal(["Touch the key", "Still waiting"], model.Messages);

        model.ShowError("pam_acct_mgmt: account expired", needsDismiss: true);
        Assert.True(model.HasError);
        Assert.True(model.NeedsDismiss);
        Assert.False(model.HasPrompt);
        var dismissed = false;
        model.Dismissed += () => dismissed = true;
        model.Submit.Execute(null);
        Assert.True(dismissed);

        model.ShowUsers(null);
        Assert.False(model.HasError);
        Assert.Empty(model.Messages);
        window.Close();
    }

    [AvaloniaFact]
    public void The_session_picker_lists_the_sessions_and_reports_a_choice()
    {
        var (window, view, model) = Show();
        var box = Named<ComboBox>(view, "SessionBox");
        Assert.Equal(2, box.ItemCount);
        SessionEntry? chosen = null;
        model.SessionChosen += session => chosen = session;
        box.SelectedIndex = 1;
        Assert.Equal("i3", chosen?.Id);
        Assert.Same(model.Sessions[1], model.SelectedSession);
        window.Close();
    }

    [AvaloniaFact]
    public void The_accessibility_panel_reports_changes_but_not_its_own_updates()
    {
        var model = new AccessibilityModel();
        var contrast = new List<bool>();
        var scales = new List<double>();
        model.HighContrastChanged += contrast.Add;
        model.TextScaleChanged += scales.Add;
        model.Show(true, 1.5);
        Assert.Empty(contrast);
        Assert.Empty(scales);
        Assert.Equal("150%", model.TextSize);
        model.HighContrast = false;
        model.TextSize = "200%";
        Assert.Equal([false], contrast);
        Assert.Equal([2.0], scales);
        Assert.Equal(1.25, AccessibilityModel.ScaleOf("125%"));
    }

    [AvaloniaFact]
    public void A_secret_text_box_reports_only_its_mask_to_assistive_technology()
    {
        var box = new Basin.UI.Avalonia.SecretTextBox { PasswordChar = '●', Text = "hunter2" };
        var window = new Window { Content = box };
        window.Show();
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(box);
        var value = peer.GetProvider<Avalonia.Automation.Provider.IValueProvider>()!;
        Assert.Equal("●●●●●●●", value.Value);
        Assert.False(value.IsReadOnly);
        value.SetValue("changed");
        Assert.Equal("hunter2", box.Text);

        box.PasswordChar = '\0';
        Assert.Equal("hunter2", value.Value);
        value.SetValue("changed");
        Assert.Equal("changed", box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Signing_in_replaces_the_form_with_the_users_tile_and_a_ring()
    {
        var (window, view, model) = Show();
        model.ShowUsers(null);
        model.SelectedUser = model.Users[0];
        model.BeginConversation("Ada Lovelace");
        model.ShowPrompt("Password:", secret: true);
        model.ShowSigningIn();

        var panel = Named<StackPanel>(view, "SigningIn");
        Assert.True(panel.IsEffectivelyVisible);
        Assert.False(Named<TextBox>(view, "Field").IsEffectivelyVisible);
        Assert.False(Named<ListBox>(view, "UserList").IsEffectivelyVisible);
        Assert.False(Named<ComboBox>(view, "SessionBox").IsEffectivelyVisible);
        Assert.Same(model.Users[0], model.SelectedUser);
        Assert.Equal("Signing in…", model.SigningInText);
        Assert.False(model.CanSubmit);
        Assert.Contains(panel.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Ada Lovelace");
        window.Close();
    }
}
