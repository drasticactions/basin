namespace Basin.Capabilities;

public readonly record struct SelectedSource(PromptSourceKinds Kind, IOutput? Output, ulong ToplevelId, int VirtualWidth = 0, int VirtualHeight = 0);
