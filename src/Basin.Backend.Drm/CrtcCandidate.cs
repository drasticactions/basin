namespace Basin.Backend.Drm;

/// <summary>
/// One connector as input to <see cref="CrtcAssignment.Solve"/>.
/// </summary>
/// <param name="PossibleCrtcs">A bit mask of the CRTC indices that the connector's encoders can drive.</param>
/// <param name="CurrentCrtcIndex">The CRTC index that the connector uses now, or -1 when it uses none.</param>
public readonly record struct CrtcCandidate(uint PossibleCrtcs, int CurrentCrtcIndex);
