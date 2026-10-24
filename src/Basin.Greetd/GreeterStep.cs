namespace Basin.Greetd;

/// <summary>
/// One transition of a <see cref="GreeterFlow"/>, as the <see cref="GreeterFlow.Stepped"/> event reports it.
/// </summary>
/// <param name="Kind">The kind of transition.</param>
/// <param name="Text">The text for the step, for example the prompt, the user, the session id or the layout id. It is <see langword="null"/> when the step has none.</param>
/// <param name="PromptType">The kind of prompt or message for a <see cref="GreeterStepKind.Prompt"/> or <see cref="GreeterStepKind.Message"/> step.</param>
/// <param name="Count">The number of users or sessions for a read step, else zero.</param>
public readonly record struct GreeterStep(GreeterStepKind Kind, string? Text = null, GreetdAuthMessageType PromptType = GreetdAuthMessageType.Visible, int Count = 0);
