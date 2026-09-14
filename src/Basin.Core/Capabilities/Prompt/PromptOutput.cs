namespace Basin.Capabilities;

public readonly record struct PromptOutput(IOutput Output, string Name, string Description, Box LayoutBox, double Scale);
