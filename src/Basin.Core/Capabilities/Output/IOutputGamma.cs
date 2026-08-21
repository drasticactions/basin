namespace Basin.Capabilities;

public interface IOutputGamma
{
    uint RampSize(IOutput output);

    bool Apply(IOutput output, in OutputGammaRamps ramps);

    bool Reset(IOutput output);
}
