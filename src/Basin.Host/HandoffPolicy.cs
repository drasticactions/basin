namespace Basin.Host;

public sealed class HandoffPolicy
{
    public HandoffPolicy(HandoffMode mode, bool greeter = false, bool greetd = false)
    {
        Mode = mode;
        Greeter = greeter;
        Greetd = greetd;
    }

    public HandoffMode Mode { get; }

    public bool Greeter { get; }

    public bool Greetd { get; }

    public bool Inherits => Mode != HandoffMode.Off;

    public bool StampsSession => Greeter && Mode != HandoffMode.Off;

    public bool RetainsOn(HandoffStop stop) => stop switch
    {
        HandoffStop.Failure => false,
        _ when Mode == HandoffMode.Retain => true,
        HandoffStop.SessionStart => Mode == HandoffMode.Auto && Greeter && Greetd,
        _ => false,
    };
}
