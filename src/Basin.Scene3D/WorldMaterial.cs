using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// The surface properties of a frame or a plaque for the Blinn-Phong shaders.
/// </summary>
/// <param name="Albedo">The linear RGBA base color. Alpha is multiplied by the window's opacity.</param>
/// <param name="SpecularStrength">The weight of the specular term. Zero gives a matte surface.</param>
/// <param name="Shininess">The Blinn-Phong exponent. A larger value gives a smaller highlight.</param>
/// <param name="Emissive">The linear RGB light that the surface gives off, added after lighting.</param>
public readonly record struct WorldMaterial(Vector4 Albedo, float SpecularStrength, float Shininess, Vector3 Emissive)
{
    /// <summary>
    /// Gets white with a specular strength of 0.25, a shininess of 24 and no emission. These are the values the lit shaders use for every other surface.
    /// </summary>
    public static WorldMaterial Default => new(Vector4.One, 0.25f, 24, Vector3.Zero);
}
