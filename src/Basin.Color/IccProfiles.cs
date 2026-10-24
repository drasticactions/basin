using Basin.Capabilities;
using Lcms2;
using Lcms2.Native;

namespace Basin.Color;

/// <summary>
/// Checks ICC profiles with Little CMS.
/// </summary>
public static class IccProfiles
{
    /// <summary>
    /// Tells whether an ICC profile is an RGB profile that Little CMS can transform to sRGB.
    /// </summary>
    /// <param name="iccData">The profile bytes.</param>
    /// <returns><see langword="false"/> when liblcms2 is absent, the profile does not parse, it is not RGB, or no transform builds.</returns>
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
