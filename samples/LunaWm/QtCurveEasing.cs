using Avalonia.Animation.Easings;

namespace LunaWm;

internal sealed class QtCurveEasing(QtCurve curve) : Easing
{
    public override double Ease(double progress) => QtEasing.Evaluate(curve, progress);
}
