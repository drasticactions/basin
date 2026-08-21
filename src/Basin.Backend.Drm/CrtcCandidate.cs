namespace Basin.Backend.Drm;

public readonly record struct CrtcCandidate(uint PossibleCrtcs, int CurrentCrtcIndex);
