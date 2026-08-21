namespace Basin.Backend.Drm;

public static class CrtcAssignment
{
    public static int[] Solve(ReadOnlySpan<CrtcCandidate> candidates, int crtcCount)
    {
        var best = new int[candidates.Length];
        var current = new int[candidates.Length];
        Array.Fill(best, -1);
        Array.Fill(current, -1);
        var bestScore = (-1, -1);

        Search(candidates, crtcCount, 0, 0u, current, ref bestScore, best);
        return best;
    }

    private static void Search(
        ReadOnlySpan<CrtcCandidate> candidates,
        int crtcCount,
        int index,
        uint used,
        int[] current,
        ref (int Lit, int Kept) bestScore,
        int[] best)
    {
        if (index == candidates.Length)
        {
            var lit = 0;
            var kept = 0;
            for (var i = 0; i < current.Length; i++)
            {
                if (current[i] >= 0)
                {
                    lit++;
                    if (current[i] == candidates[i].CurrentCrtcIndex)
                    {
                        kept++;
                    }
                }
            }

            if ((lit, kept).CompareTo(bestScore) > 0)
            {
                bestScore = (lit, kept);
                current.CopyTo(best, 0);
            }

            return;
        }

        var candidate = candidates[index];

        if (candidate.CurrentCrtcIndex >= 0)
        {
            TryCrtc(candidates, crtcCount, index, used, current, ref bestScore, best, candidate.CurrentCrtcIndex);
        }

        for (var crtc = 0; crtc < crtcCount; crtc++)
        {
            if (crtc != candidate.CurrentCrtcIndex)
            {
                TryCrtc(candidates, crtcCount, index, used, current, ref bestScore, best, crtc);
            }
        }

        current[index] = -1;
        Search(candidates, crtcCount, index + 1, used, current, ref bestScore, best);
    }

    private static void TryCrtc(
        ReadOnlySpan<CrtcCandidate> candidates,
        int crtcCount,
        int index,
        uint used,
        int[] current,
        ref (int Lit, int Kept) bestScore,
        int[] best,
        int crtc)
    {
        var bit = 1u << crtc;
        if (crtc >= crtcCount || (candidates[index].PossibleCrtcs & bit) == 0 || (used & bit) != 0)
        {
            return;
        }

        current[index] = crtc;
        Search(candidates, crtcCount, index + 1, used | bit, current, ref bestScore, best);
        current[index] = -1;
    }
}
