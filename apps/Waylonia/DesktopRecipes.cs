namespace Waylonia;

internal static class DesktopRecipes
{
    public static IReadOnlyList<DesktopRecipe> All { get; } =
    [
        new DesktopRecipe(
            "sway", "sway", "sway", [], Bus: true, Gpu: false, Video: null, SoftwareFallback: true),
        new DesktopRecipe(
            "niri", "niri", "niri", [], Bus: true, Gpu: true, Video: null, SoftwareFallback: false),
        new DesktopRecipe(
            "plasma",
            "startplasma-wayland",
            "KDE",
            ["XDG_SESSION_DESKTOP=KDE"],
            Bus: true,
            Gpu: true,
            Video: null,
            SoftwareFallback: false),
        new DesktopRecipe(
            "cosmic", "cosmic-session", "COSMIC", [], Bus: true, Gpu: true, Video: null, SoftwareFallback: false),
        new DesktopRecipe(
            "xfce",
            "cage -- startxfce4",
            "XFCE",
            [],
            Bus: true,
            Gpu: false,
            Video: null,
            SoftwareFallback: false),
    ];

    public static DesktopRecipe? Find(string name)
    {
        foreach (var recipe in All)
        {
            if (string.Equals(recipe.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return recipe;
            }
        }

        return null;
    }

    public static string Names => string.Join(", ", All.Select(static recipe => recipe.Name));
}
