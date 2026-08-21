using Pixman;

namespace Basin;

public readonly record struct PixelShaderUniformValue(float X, float Y = 0f, float Z = 0f, float W = 0f)
{
    public static implicit operator PixelShaderUniformValue(float value) => new(value);

    public static implicit operator PixelShaderUniformValue((float X, float Y) value) => new(value.X, value.Y);

    public static implicit operator PixelShaderUniformValue((float X, float Y, float Z) value) => new(value.X, value.Y, value.Z);

    public static implicit operator PixelShaderUniformValue((float X, float Y, float Z, float W) value) => new(value.X, value.Y, value.Z, value.W);
}
