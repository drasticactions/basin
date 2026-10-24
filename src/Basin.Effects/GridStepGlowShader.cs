namespace Basin.Effects;

/// <summary>
/// Holds the analytic glow shader for the step overview grid. It writes premultiplied color with alpha 0, so every shader row adds it to the target.
/// </summary>
public static class GridStepGlowShader
{
    /// <summary>
    /// The number of <c>vec4</c> uniforms that <see cref="Pack"/> writes.
    /// </summary>
    public const int UniformCount = 29;

    /// <summary>
    /// The uniforms of the shader: five step map vectors, two bulge vectors, ten light boxes and twelve scalar vectors.
    /// </summary>
    public static readonly PixelShaderUniform[] Uniforms =
    [
        new("m0", PixelShaderUniformType.Float4),
        new("m1", PixelShaderUniformType.Float4),
        new("m2", PixelShaderUniformType.Float4),
        new("m3", PixelShaderUniformType.Float4),
        new("m4", PixelShaderUniformType.Float4),
        new("b0", PixelShaderUniformType.Float4),
        new("b1", PixelShaderUniformType.Float4),
        new("l0", PixelShaderUniformType.Float4),
        new("l1", PixelShaderUniformType.Float4),
        new("l2", PixelShaderUniformType.Float4),
        new("l3", PixelShaderUniformType.Float4),
        new("l4", PixelShaderUniformType.Float4),
        new("l5", PixelShaderUniformType.Float4),
        new("l6", PixelShaderUniformType.Float4),
        new("l7", PixelShaderUniformType.Float4),
        new("l8", PixelShaderUniformType.Float4),
        new("l9", PixelShaderUniformType.Float4),
        new("s0", PixelShaderUniformType.Float4),
        new("s1", PixelShaderUniformType.Float4),
        new("s2", PixelShaderUniformType.Float4),
        new("s3", PixelShaderUniformType.Float4),
        new("s4", PixelShaderUniformType.Float4),
        new("s5", PixelShaderUniformType.Float4),
        new("s6", PixelShaderUniformType.Float4),
        new("s7", PixelShaderUniformType.Float4),
        new("s8", PixelShaderUniformType.Float4),
        new("s9", PixelShaderUniformType.Float4),
        new("s10", PixelShaderUniformType.Float4),
        new("s11", PixelShaderUniformType.Float4),
    ];

    private static byte[]? _spirv;

    /// <summary>
    /// Gets the shader source for every dialect. The SPIR-V loads from an embedded resource on the first read.
    /// </summary>
    /// <exception cref="InvalidOperationException">The SPIR-V resource is missing from the assembly.</exception>
    public static PixelShaderSource Source => new()
    {
        Glsl = GridGlowCommon.Chunk + Body + GlslMain,
        Sksl = GridGlowCommon.Chunk + Body + SkslMain,
        SpirV = _spirv ??= LoadSpirV(),
    };

    internal const string Body = """

        bool st_has(float bits, float bit) {
            return mod(floor(bits / bit), 2.0) >= 1.0;
        }

        float st_wall_width(int k) {
            float bit = k == 1 ? 1.0 : k == 2 ? 2.0 : k == 3 ? 4.0 : 8.0;
            if (!st_has(m4.x, bit)) {
                return 0.0;
            }

            return k == 1 ? m1.x - m2.x
                : k == 2 ? (m2.x + m2.z) - (m1.x + m1.z)
                : k == 3 ? m1.y - m2.y
                : (m2.y + m2.w) - (m1.y + m1.w);
        }

        vec4 st_bulge(int k) {
            return k == 1 ? vec4(b0.xy, 0.0, 0.0) : k == 2 ? vec4(b0.zw, 0.0, 0.0) : k == 3 ? vec4(b1.xy, 0.0, 0.0) : vec4(b1.zw, 0.0, 0.0);
        }

        vec4 grid_step_glow(vec2 coord, vec2 size) {
            vec2 p = s8.yz + coord * vec2(s8.w, s9.x) / size;
            bool lines = st_has(s6.w, 1.0);
            bool shelfLines = st_has(s6.w, 2.0);
            bool desktopLines = st_has(s6.w, 4.0);
            bool density = st_has(s6.w, 8.0);
            bool wallLines = st_has(s6.w, 16.0);
            if (!lines || p.x < m3.x || p.x >= m3.x + m3.z || p.y < m3.y || p.y >= m3.y + m3.w) {
                return vec4(0.0);
            }

            float across = -1000000.0;
            float along = -1000000.0;
            int horizontal = 0;
            int vertical = 0;
            float width = st_wall_width(1);
            if (width > 0.0 && -(p.x - m1.x) / width > across) {
                across = -(p.x - m1.x) / width;
                horizontal = 1;
            }

            width = st_wall_width(2);
            if (width > 0.0 && (p.x - (m1.x + m1.z)) / width > across) {
                across = (p.x - (m1.x + m1.z)) / width;
                horizontal = 2;
            }

            width = st_wall_width(3);
            if (width > 0.0 && -(p.y - m1.y) / width > along) {
                along = -(p.y - m1.y) / width;
                vertical = 3;
            }

            width = st_wall_width(4);
            if (width > 0.0 && (p.y - (m1.y + m1.w)) / width > along) {
                along = (p.y - (m1.y + m1.w)) / width;
                vertical = 4;
            }

            float depth = max(across, along);
            int wall = across >= along ? horizontal : vertical;
            bool plane = horizontal == 0 && vertical == 0 || depth <= 0.0;
            float cell = s0.w;
            if (plane || depth >= 1.0) {
                float scale = m0.z;
                if (!plane) {
                    if (across >= 1.0 && along >= 1.0) {
                        bool h = horizontal == 1 ? st_has(m4.y, 1.0) : st_has(m4.y, 2.0);
                        bool v = vertical == 3 ? st_has(m4.y, 4.0) : st_has(m4.y, 8.0);
                        if (!(h && v)) {
                            return vec4(0.0);
                        }
                    } else {
                        float bit = wall == 1 ? 1.0 : wall == 2 ? 2.0 : wall == 3 ? 4.0 : 8.0;
                        if (!st_has(m4.y, bit)) {
                            return vec4(0.0);
                        }
                    }

                    if (!shelfLines) {
                        return vec4(0.0);
                    }

                    scale = m0.w;
                } else if (!desktopLines) {
                    return vec4(0.0);
                }

                float step = cell * g_stride(scale);
                vec2 c = m0.xy + (p - m0.xy) / scale;
                vec2 nearest = floor(c / step + 0.5) * step;
                float dColumn = max(0.0, abs((c.x - nearest.x) * scale - 0.5) - 0.5);
                float dRow = max(0.0, abs((c.y - nearest.y) * scale - 0.5) - 0.5);
                return g_glow(p, min(dColumn, dRow), step * scale, dColumn <= dRow, density);
            }

            if (!wallLines || wall == 0) {
                return vec4(0.0);
            }

            width = st_wall_width(wall);
            float s = depth;
            float q = clamp(floor(s * 4.0 + 0.5), 1.0, 3.0) / 4.0;
            float dQuarter = max(0.0, abs(s - q) * width - 0.5);
            bool sideWall = wall == 1 || wall == 2;
            float a = sideWall ? p.y : p.x;
            float innerStart = sideWall ? m1.y : m1.x;
            float innerLength = sideWall ? m1.w : m1.z;
            float outerStart = sideWall ? m2.y : m2.x;
            float outerLength = sideWall ? m2.w : m2.z;
            vec4 bulge = st_bulge(wall);
            float k = clamp(bulge.y, 0.0, 0.7) * 4.0 * s * (1.0 - s);
            float span = (1.0 - s) * innerLength + s * outerLength;
            float spanStart = (1.0 - s) * innerStart + s * outerStart;
            float stretch = 1.0;
            if (k > 0.0 && span > 0.0) {
                float bent = a;
                for (int i = 0; i < 8; i++) {
                    float fraction = clamp((a - spanStart) / span, 0.0, 1.0);
                    float envelope = 4.0 * fraction * (1.0 - fraction);
                    float slope = 4.0 * (1.0 - 2.0 * fraction) / span;
                    a = a - (a + k * envelope * (bulge.x - a) - bent) / (1.0 + k * (slope * (bulge.x - a) - envelope));
                }

                float at = clamp((a - spanStart) / span, 0.0, 1.0);
                stretch = 1.0 + k * (4.0 * (1.0 - 2.0 * at) / span * (bulge.x - a) - 4.0 * at * (1.0 - at));
            }

            float t = (a - spanStart) / span;
            float center = sideWall ? m0.y : m0.x;
            float canvas = center + (innerStart + t * innerLength - center) / m0.z;
            float step = cell * g_stride(m0.z);
            float nearest = floor(canvas / step + 0.5) * step;
            float fan = innerLength > 0.0 ? span / innerLength : 1.0;
            float unit = m0.z * fan * stretch;
            float dCross = max(0.0, abs(canvas - nearest) * unit - 0.5);
            if (dQuarter <= dCross) {
                return g_glow(p, dQuarter, width / 4.0, sideWall, density);
            }

            return g_glow(p, dCross, step * unit, !sideWall, density);
        }
        """;

    private const string GlslMain = """

        vec4 basin_pixel(vec2 coord) {
            return grid_step_glow(coord, u_size);
        }
        """;

    private const string SkslMain = """

        half4 basin_pixel(float2 coord) {
            return half4(grid_step_glow(coord, u_size));
        }
        """;

    /// <summary>
    /// Writes the uniforms for a step overview grid from its step map, lights, effects and reveal. A grid with no map gets all zeros, which draws no glow.
    /// </summary>
    /// <param name="grid">The grid source to describe.</param>
    /// <param name="bounds">The bounds of the grid mesh, in output pixels.</param>
    /// <param name="into">The destination, with at least <see cref="UniformCount"/> values.</param>
    /// <exception cref="ArgumentException"><paramref name="into"/> is shorter than <see cref="UniformCount"/>.</exception>
    public static void Pack(CanvasStepSource grid, in Box bounds, Span<PixelShaderUniformValue> into)
    {
        if (into.Length < UniformCount)
        {
            throw new ArgumentException("the uniform span is shorter than the glow's uniform list", nameof(into));
        }

        var map = grid.Map;
        if (map is null)
        {
            into[..UniformCount].Clear();
            return;
        }

        var outline = map.Outline;
        var inner = map.Inner;
        var outer = map.Outer;
        into[0] = new((float)map.CenterX, (float)map.CenterY, (float)map.Zoom, (float)map.ShelfScale);
        into[1] = new((float)outline.X, (float)outline.Y, (float)outline.Width, (float)outline.Height);
        into[2] = new((float)inner.X, (float)inner.Y, (float)inner.Width, (float)inner.Height);
        into[3] = new((float)outer.X, (float)outer.Y, (float)outer.Width, (float)outer.Height);
        into[4] = new((float)map.Sides, (float)map.Shelves, 0f, grid.CellSize);
        into[5] = new(
            (float)map.BulgeCenter(CanvasStepSides.Left), (float)map.BulgeDepth(CanvasStepSides.Left),
            (float)map.BulgeCenter(CanvasStepSides.Right), (float)map.BulgeDepth(CanvasStepSides.Right));
        into[6] = new(
            (float)map.BulgeCenter(CanvasStepSides.Top), (float)map.BulgeDepth(CanvasStepSides.Top),
            (float)map.BulgeCenter(CanvasStepSides.Bottom), (float)map.BulgeDepth(CanvasStepSides.Bottom));
        var effects = grid.Effects;
        var lines = grid.Lines && grid.Alpha > 0f;
        var flags = (lines ? 1 : 0) + (grid.ShelfLines ? 2 : 0) + (grid.DesktopLines ? 4 : 0) + (effects.Density ? 8 : 0) +
            (grid.WallGridLines ? 16 : 0);
        GridGlowCommon.Pack(
            grid.Lights, effects, grid.Reveal, grid.Color, grid.Alpha, bounds, grid.MinLineSpacing, flags,
            new(1f, 0f, 0f, grid.CellSize), default,
            into.Slice(7, GridGlowCommon.Lights), into.Slice(17, GridGlowCommon.Scalars));
    }

    private static byte[] LoadSpirV()
    {
        using var stream = typeof(GridStepGlowShader).Assembly.GetManifestResourceStream("grid_step_glow.frag.spv")
            ?? throw new InvalidOperationException("missing shader resource grid_step_glow.frag.spv");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }
}
