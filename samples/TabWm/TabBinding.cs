using Basin.Config;

namespace TabWm;

internal sealed record TabBinding(uint Keysym, Modifiers Modifiers, TabAction Action, int Argument = 0, string[]? Command = null, string Chord = "", string Label = "");
