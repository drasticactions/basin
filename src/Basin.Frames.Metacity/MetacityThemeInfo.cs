namespace Basin.Frames.Metacity;

/// <summary>
/// The five required children of a theme's <c>info</c> element.
/// </summary>
/// <param name="Name">The readable theme name.</param>
/// <param name="Author">The author.</param>
/// <param name="Copyright">The copyright notice.</param>
/// <param name="Date">The date as the theme writes it.</param>
/// <param name="Description">The description.</param>
public readonly record struct MetacityThemeInfo(string Name, string Author, string Copyright, string Date, string Description);
