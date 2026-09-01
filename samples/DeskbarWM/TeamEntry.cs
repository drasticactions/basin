using SkiaSharp;

namespace DeskbarWm;

internal sealed record TeamEntry(Team Team, string Label, SKImage? Icon, bool Active, bool Hidden, bool Expanded) : BarRow;
