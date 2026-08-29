namespace Basin.Capabilities;

public interface IOutputColorPipeline
{
    uint DegammaLutSize { get; }

    uint GammaLutSize { get; }

    bool SupportsCtm { get; }
}
