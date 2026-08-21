namespace Basin.Cli;

public readonly record struct TransportChoice(TransportKind Kind)
{
    public override string ToString() => Kind == TransportKind.Managed ? "managed" : "libwayland";
}
