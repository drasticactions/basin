namespace Basin.Capabilities;

[Flags]
public enum ColorFeatures
{
    None = 0,

    Parametric = 1,

    IccProfiles = 2,

    CustomPrimaries = 4,

    Luminances = 8,

    Transforms = 16,
}
