using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace EightWm;

internal static class StillCaret
{
    public const string Variable = "BASIN_STILL_CARET";

    public static void Apply(Application? app)
    {
        if (app is null || Environment.GetEnvironmentVariable(Variable) != "1")
        {
            return;
        }

        app.Styles.Add(new Style(static selector => selector.Is<TextBox>())
        {
            Setters = { new Setter(TextBox.CaretBlinkIntervalProperty, TimeSpan.Zero) },
        });
    }
}
