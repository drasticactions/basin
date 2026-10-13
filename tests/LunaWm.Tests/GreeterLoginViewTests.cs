using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace LunaWm.Tests;

public sealed class GreeterLoginViewTests
{
    [AvaloniaFact]
    public void Signing_in_fades_the_pad_and_the_strip_out_and_stops_taking_input()
    {
        var model = new GreeterLoginModel();
        model.Users.Add(new GreeterUserEntry("ada", "Ada Lovelace", null));
        var view = new GreeterLoginView { DataContext = model };
        var window = new Window { Content = view, Width = 1024, Height = 768 };
        window.Show();
        model.BeginConversation("Ada Lovelace");
        model.ShowPrompt("Password:", secret: true);
        var content = view.FindControl<StackPanel>("LoginContent")!;
        Assert.Equal(1.0, model.ContentOpacity);
        Assert.True(content.IsHitTestVisible);

        model.ShowSigningIn();
        Assert.True(model.SigningIn);
        Assert.Equal(0.0, model.ContentOpacity);
        Assert.False(content.IsHitTestVisible);
        Assert.False(model.CanSubmit);
        var fade = Assert.Single(content.Transitions!.OfType<Avalonia.Animation.DoubleTransition>());
        Assert.Equal(TimeSpan.FromMilliseconds(200), fade.Duration);
        window.Close();
    }
}
