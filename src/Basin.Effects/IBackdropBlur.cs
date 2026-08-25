using Basin.Capabilities;

namespace Basin.Effects;

public interface IBackdropBlur : IBackdropEffect, IBackgroundEffects, IDisposable
{
    BlurOptions Options { get; set; }

    BlurCorners Corners { get; set; }

    double Opacity { get; set; }

    int ExpandSize { get; }

    void SetSurface(object key, in BlurSurfaceOptions options);

    bool ForgetSurface(object key);
}
