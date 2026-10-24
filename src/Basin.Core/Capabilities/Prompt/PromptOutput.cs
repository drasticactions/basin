namespace Basin.Capabilities;

/// <summary>
/// Describes one output that a source picker can offer.
/// </summary>
/// <param name="Output">The output that a choice of this row captures.</param>
/// <param name="Name">The connector name, for example <c>DP-1</c>.</param>
/// <param name="Description">The human-readable output description, for example the make and model.</param>
/// <param name="LayoutBox">The position and the logical size of the output in the layout.</param>
/// <param name="Scale">The output scale factor.</param>
public readonly record struct PromptOutput(IOutput Output, string Name, string Description, Box LayoutBox, double Scale);
