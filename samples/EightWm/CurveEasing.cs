using Avalonia.Animation.Easings;

namespace EightWm;

internal sealed class CurveEasing(AnimationCurve curve) : Easing
{
    public override double Ease(double progress) => Curves.Evaluate(curve, progress);
}
