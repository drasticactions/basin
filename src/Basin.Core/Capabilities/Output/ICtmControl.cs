namespace Basin.Capabilities;

public interface ICtmControl
{
    bool SupportsCtm(IOutput output);

    bool SetCtm(IOutput output, ReadOnlySpan<double> rowMajor3x3);

    bool ResetCtm(IOutput output);
}
