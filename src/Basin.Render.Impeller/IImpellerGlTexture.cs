using Basin.Diagnostics;
using Basin.Render.Gl;
using NImpeller;
using Silk.NET.OpenGLES;

namespace Basin.Render.Impeller;

internal interface IImpellerGlTexture : ITexture
{
    bool Acquire(out IntPtr texture);
}
