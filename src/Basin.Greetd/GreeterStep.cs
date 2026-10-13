namespace Basin.Greetd;

public readonly record struct GreeterStep(GreeterStepKind Kind, string? Text = null, GreetdAuthMessageType PromptType = GreetdAuthMessageType.Visible, int Count = 0);
