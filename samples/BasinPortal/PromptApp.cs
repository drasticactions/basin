using Avalonia;
using Avalonia.Fonts.Inter;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace BasinPortal;

internal sealed class PromptApp : Application
{
    public override void Initialize()
    {
        AvaloniaLocator.CurrentMutable.Bind<FontManagerOptions>().ToConstant(new FontManagerOptions
        {
            DefaultFamilyName = "fonts:Inter#Inter",
        });
        FontManager.Current.AddFontCollection(new InterFontCollection());
        Styles.Add(new FluentTheme());
    }
}
