using System.Runtime.InteropServices;
using Mesa.Egl;
using Mesa.Gbm;
using Mesa.Native;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

internal static class EglDmabuf
{
    public const uint LinuxDmaBufExt = 0x3270;
    public const nint Width = 0x3057;
    public const nint Height = 0x3056;
    public const nint Fourcc = 0x3271;
    public static readonly nint[] PlaneFd = [0x3272, 0x3275, 0x3278, 0x3440];
    public static readonly nint[] PlaneOffset = [0x3273, 0x3276, 0x3279, 0x3441];
    public static readonly nint[] PlanePitch = [0x3274, 0x3277, 0x327A, 0x3442];
    public static readonly nint[] PlaneModifierLo = [0x3443, 0x3445, 0x3447, 0x3449];
    public static readonly nint[] PlaneModifierHi = [0x3444, 0x3446, 0x3448, 0x344A];
}
