namespace Basin.Color;

public static class Bt2446a
{
    private const double KR = 0.262698338956556;
    private const double KG = 0.678008765772817;
    private const double KB = 0.0592928952706273;
    private const double RHelper = 1.47460332208689;
    private const double BHelper = 1.88141420945875;
    private const double InverseGamma = 2.4;
    private const double Gamma = 1 / InverseGamma;

    public static void Expand(Span<double> rgb, double sdrNits, double targetNits)
    {
        sdrNits = Math.Clamp(sdrNits, 1, 1000);
        targetNits = Math.Clamp(targetNits, sdrNits, 10000);
        var r = Math.Pow(Math.Max(0, rgb[0]), Gamma);
        var g = Math.Pow(Math.Max(0, rgb[1]), Gamma);
        var b = Math.Pow(Math.Max(0, rgb[2]), Gamma);
        var yTmo = (KR * r) + (KG * g) + (KB * b);
        var cbTmo = (b - yTmo) / BHelper;
        var crTmo = (r - yTmo) / RHelper;

        if (sdrNits > 99 && sdrNits < 101 && targetNits > 999 && targetNits < 1001)
        {
            const double a1 = 1.8712e-5;
            const double b1 = -2.7334e-3;
            const double c1 = 1.3141;
            const double a2 = 2.8305e-6;
            const double b2 = -7.4622e-4;
            const double c2 = 1.2328;
            var yy = 255.0 * yTmo;
            var e = yy <= 70 ? (a1 * yy * yy) + (b1 * yy) + c1 : (a2 * yy * yy) + (b2 * yy) + c2;
            var yHdr = Math.Pow(yy, e);
            var sc = yTmo > 0 ? 1.075 * (yHdr / yTmo) : 1;
            var cbHdr = cbTmo * sc;
            var crHdr = crTmo * sc;
            r = Math.Clamp(yHdr + (RHelper * crHdr), 0, 1000) / 1000;
            g = Math.Clamp(yHdr - (0.16455312684366 * cbHdr) - (0.57135312684366 * crHdr), 0, 1000) / 1000;
            b = Math.Clamp(yHdr + (BHelper * cbHdr), 0, 1000) / 1000;
            targetNits = 1000;
        }
        else
        {
            var ySdr = yTmo + Math.Max(0.1 * crTmo, 0);
            var pSdr = 1 + (32 * Math.Pow(sdrNits / 10000, Gamma));
            var yC = Math.Log((ySdr * (pSdr - 1)) + 1) / Math.Log(pSdr);
            var yP0 = yC / 1.0770;
            var yP2 = (yC - 0.5) / 0.5;
            var yP1 = (-2.7811 + Math.Sqrt(Math.Max(0, 4.83307641 - (4.604 * yC)))) / -2.302;
            double yP;
            if (yP0 <= 0.7399)
            {
                yP = yP0;
            }
            else if (yP1 > 0.7399 && yP1 < 0.9909)
            {
                yP = yP1;
            }
            else if (yP2 >= 0.9909)
            {
                yP = yP2;
            }
            else
            {
                yP = yP1;
            }

            var pHdr = 1 + (32 * Math.Pow(targetNits / 10000, Gamma));
            var y = (Math.Pow(pHdr, yP) - 1) / (pHdr - 1);
            var scale = y > 0 ? ySdr / (1.1 * y) : 0;
            if (scale > 0)
            {
                b = (cbTmo * BHelper / scale) + y;
                r = (crTmo * RHelper / scale) + y;
            }
            else
            {
                b = y;
                r = y;
            }

            g = (y - ((KR * r) + (KB * b))) / KG;
            r = Math.Clamp(r, 0, 1);
            g = Math.Clamp(g, 0, 1);
            b = Math.Clamp(b, 0, 1);
        }

        rgb[0] = Math.Pow(r, InverseGamma) * targetNits;
        rgb[1] = Math.Pow(g, InverseGamma) * targetNits;
        rgb[2] = Math.Pow(b, InverseGamma) * targetNits;
    }
}
