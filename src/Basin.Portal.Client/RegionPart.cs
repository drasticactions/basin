using Basin;

namespace Basin.Portal.Client;

internal readonly record struct RegionPart(int Output, Box Destination, double SourceX, double SourceY, double Ratio);
