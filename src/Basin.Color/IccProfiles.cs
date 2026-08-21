using Basin.Capabilities;
using Lcms2;
using Lcms2.Native;

namespace Basin.Color;

public static class IccProfiles
{
    public static bool Validate(byte[] iccData)
    {
        if (!Lcms2Support.IsAvailable)
        {
            return false;
        }

        try
        {
            using var profile = IccProfile.FromMemory(iccData);
            if (profile.ColorSpace != cmsColorSpaceSignature.cmsSigRgbData)
            {
                return false;
            }

            using var srgb = IccProfile.CreateSrgb();
            using var transform = ColorTransform.Create(
                profile, PixelFormat.RgbFloat, srgb, PixelFormat.RgbFloat, RenderingIntent.Perceptual);
            return true;
        }
        catch (Lcms2Exception)
        {
            return false;
        }
    }
}
