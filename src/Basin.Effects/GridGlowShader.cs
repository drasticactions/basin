namespace Basin.Effects;

/// <summary>
/// Holds the analytic glow shader for a canvas grid in warp, terrace and scale mode. It writes premultiplied color with alpha 0, so every shader row adds it to the target.
/// </summary>
public static class GridGlowShader
{
    /// <summary>
    /// The number of <c>vec4</c> uniforms that <see cref="Pack"/> writes.
    /// </summary>
    public const int UniformCount = 30;

    /// <summary>
    /// The uniforms of the shader: two vectors per warp zone, ten light boxes and twelve scalar vectors.
    /// </summary>
    public static readonly PixelShaderUniform[] Uniforms =
    [
        new("w0a", PixelShaderUniformType.Float4),
        new("w0b", PixelShaderUniformType.Float4),
        new("w1a", PixelShaderUniformType.Float4),
        new("w1b", PixelShaderUniformType.Float4),
        new("w2a", PixelShaderUniformType.Float4),
        new("w2b", PixelShaderUniformType.Float4),
        new("w3a", PixelShaderUniformType.Float4),
        new("w3b", PixelShaderUniformType.Float4),
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

        bool gw_on(vec4 a) {
            return a.y > 0.0 && a.z > 0.0;
        }

        bool gw_has(vec4 a, float dir, float x) {
            return gw_on(a) && dir * (x - a.x) > 0.0;
        }

        bool gw_inside(vec4 a, float dir, float x) {
            return !gw_on(a) || dir * (x - a.x) < 0.0;
        }

        float gw_dist(vec4 a, vec4 b, float u) {
            float p1 = b.x + 1.0;
            return a.z * (a.w * u + (1.0 - a.w) * (1.0 - pow(max(1.0 - u, 0.0), p1)) / p1);
        }

        float gw_scale_u(vec4 a, vec4 b, float u) {
            if (u <= 0.0) {
                return 1.0;
            }

            if (u >= 1.0) {
                return a.w;
            }

            return a.w + (1.0 - a.w) * pow(1.0 - u, b.x);
        }

        float gw_scale(vec4 a, vec4 b, float dir, float x) {
            return gw_on(a) ? gw_scale_u(a, b, dir * (x - a.x) / a.z) : 1.0;
        }

        float gw_to_screen(vec4 a, vec4 b, float dir, float x) {
            if (!gw_on(a)) {
                return x;
            }

            float u = dir * (x - a.x) / a.z;
            if (u <= 0.0) {
                return x;
            }

            if (u >= 1.0) {
                return a.x + dir * (a.y + a.w * (u - 1.0) * a.z);
            }

            return a.x + dir * gw_dist(a, b, u);
        }

        float gw_to_canvas(vec4 a, vec4 b, float dir, float x) {
            if (!gw_on(a)) {
                return x;
            }

            float d = dir * (x - a.x);
            if (d <= 0.0) {
                return x;
            }

            if (d >= a.y) {
                return a.x + dir * a.z + dir * (d - a.y) / a.w;
            }

            float u = clamp(d / a.z, 0.0, 1.0);
            for (int i = 0; i < 12; i++) {
                u = clamp(u - (gw_dist(a, b, u) - d) / (a.z * gw_scale_u(a, b, u)), 0.0, 1.0);
            }

            return a.x + dir * u * a.z;
        }

        float gw_fan_t(vec4 a, vec4 b, float t) {
            if (b.w > 0.5) {
                if (t <= 0.0) {
                    return 1.0;
                }

                if (t >= 1.0) {
                    return a.w;
                }

                float bump = 16.0 * t * t * (1.0 - t) * (1.0 - t);
                return 1.0 + (a.w - 1.0) * smoothstep(0.0, 1.0, t) + b.y * bump;
            }

            return 1.0 + b.y * smoothstep(0.0, 1.0, t);
        }

        float gw_fan(vec4 a, vec4 b, float dir, float x) {
            if (!gw_on(a) || (b.y == 0.0 && b.w < 0.5)) {
                return 1.0;
            }

            float u = dir * (x - a.x) / a.z;
            if (u <= 0.0) {
                return 1.0;
            }

            if (u >= 1.0) {
                return b.w > 0.5 ? a.w : 1.0 + b.y;
            }

            return gw_fan_t(a, b, gw_dist(a, b, u) / a.y);
        }

        float gw_fan_screen(vec4 a, vec4 b, float dir, float x) {
            if (!gw_on(a) || (b.y == 0.0 && b.w < 0.5)) {
                return 1.0;
            }

            float d = dir * (x - a.x);
            if (d <= 0.0) {
                return 1.0;
            }

            if (d >= a.y) {
                return b.w > 0.5 ? a.w : 1.0 + b.y;
            }

            return gw_fan_t(a, b, d / a.y);
        }

        float gw_depth(vec4 a, vec4 b, float dir, float x) {
            if (!gw_on(a)) {
                return 0.0;
            }

            float u = dir * (x - a.x) / a.z;
            if (u <= 0.0) {
                return 0.0;
            }

            return u >= 1.0 ? 1.0 : gw_dist(a, b, u) / a.y;
        }

        float gw_depth_screen(vec4 a, float dir, float x) {
            return gw_on(a) ? clamp(dir * (x - a.x) / a.y, 0.0, 1.0) : 0.0;
        }

        int g_side_at(float x) {
            return gw_has(w0a, -1.0, x) ? 1 : gw_has(w1a, 1.0, x) ? 2 : 0;
        }

        int g_end_at(float y) {
            return gw_has(w2a, -1.0, y) ? 3 : gw_has(w3a, 1.0, y) ? 4 : 0;
        }

        void g_warp(int k, out vec4 a, out vec4 b, out float dir) {
            a = k == 1 ? w0a : k == 2 ? w1a : k == 3 ? w2a : w3a;
            b = k == 1 ? w0b : k == 2 ? w1b : k == 3 ? w2b : w3b;
            dir = k == 1 || k == 3 ? -1.0 : 1.0;
        }

        float g_row_fan(float center, float fan, float depth) {
            fan = min(fan, s1.z);
            if (gw_on(w2a) && center > w2a.x) {
                fan = min(fan, 1.0 + 0.5 * w2a.y * depth / (center - w2a.x));
            }

            if (gw_on(w3a) && w3a.x > center) {
                fan = min(fan, 1.0 + 0.5 * w3a.y * depth / (w3a.x - center));
            }

            return fan;
        }

        float g_column_fan(float center, float fan, float depth) {
            fan = min(fan, s1.w);
            if (gw_on(w0a) && center > w0a.x) {
                fan = min(fan, 1.0 + 0.5 * w0a.y * depth / (center - w0a.x));
            }

            if (gw_on(w1a) && w1a.x > center) {
                fan = min(fan, 1.0 + 0.5 * w1a.y * depth / (w1a.x - center));
            }

            return fan;
        }

        float g_radius(float level) {
            return s1.y > 0.5 ? s1.x * (1.0 - clamp(level, 0.0, 1.0)) : s1.x;
        }

        vec4 g_ring(vec4 sa, vec4 sb, float sd, vec4 ea, vec4 eb, float ed, float depth) {
            float sideCanvas = sa.x + sd * depth * sa.z;
            float edgeX = gw_to_screen(sa, sb, sd, sideCanvas);
            float edgeY = sb.z + (ea.x - sb.z) * g_row_fan(sb.z, gw_fan(sa, sb, sd, sideCanvas), gw_depth(sa, sb, sd, sideCanvas));
            float endCanvas = ea.x + ed * depth * ea.z;
            float rimY = gw_to_screen(ea, eb, ed, endCanvas);
            float rimX = eb.z + (sa.x - eb.z) * g_column_fan(eb.z, gw_fan(ea, eb, ed, endCanvas), gw_depth(ea, eb, ed, endCanvas));
            return vec4(edgeX, edgeY, rimX, rimY);
        }

        float g_outside(vec4 sa, vec4 sb, float sd, vec4 ea, vec4 eb, float ed, vec2 p, float depth) {
            vec4 ring = g_ring(sa, sb, sd, ea, eb, ed, depth);
            float spanY = ring.y - ring.w;
            float spanX = ring.z - ring.x;
            if (spanX == 0.0 || spanY == 0.0) {
                return 1.0;
            }

            float toEdge = (p.y - ring.w) / spanY;
            float toRim = (p.x - ring.x) / spanX;
            if (toEdge < 0.0 || toRim < 0.0) {
                return 1.0;
            }

            float radius = g_radius(depth);
            if (toEdge >= radius || toRim >= radius) {
                return -1.0;
            }

            float dx = toRim - radius;
            float dy = toEdge - radius;
            return dx * dx + dy * dy - radius * radius;
        }

        vec2 g_corner(vec4 sa, vec4 sb, float sd, vec4 ea, vec4 eb, float ed, vec2 p) {
            float depth = 0.0;
            if (s1.x <= 0.0) {
                float across = sd * (gw_to_canvas(sa, sb, sd, p.x) - sa.x) / sa.z;
                float along = ed * (gw_to_canvas(ea, eb, ed, p.y) - ea.x) / ea.z;
                depth = max(0.0, max(across, along));
            } else {
                float low = 0.0;
                float high = 1.0;
                bool grow = true;
                for (int i = 0; i < 6; i++) {
                    if (grow && g_outside(sa, sb, sd, ea, eb, ed, p, high) > 0.0) {
                        low = high;
                        high = high * 2.0;
                    } else {
                        grow = false;
                    }
                }

                for (int i = 0; i < 20; i++) {
                    float middle = 0.5 * (low + high);
                    if (g_outside(sa, sb, sd, ea, eb, ed, p, middle) > 0.0) {
                        low = middle;
                    } else {
                        high = middle;
                    }
                }

                depth = high;
            }

            vec4 ring = g_ring(sa, sb, sd, ea, eb, ed, depth);
            float spanY = ring.y - ring.w;
            float spanX = ring.z - ring.x;
            float toEdge = spanY != 0.0 ? clamp((p.y - ring.w) / spanY, 0.0, 1.0) : 0.0;
            float toRim = spanX != 0.0 ? clamp((p.x - ring.x) / spanX, 0.0, 1.0) : 0.0;
            float radius = g_radius(depth);
            float straight = 1.0 - radius;
            float ux = 0.0;
            float uy = 0.0;
            if (toRim <= toEdge && toEdge >= radius) {
                ux = depth;
                uy = (1.0 - toEdge) * depth;
            } else if (toRim >= radius) {
                ux = (1.0 - toRim) * depth;
                uy = depth;
            } else {
                float angle = atan(1.0 - toEdge / radius, 1.0 - toRim / radius);
                ux = depth * (straight + radius * cos(angle));
                uy = depth * (straight + radius * sin(angle));
            }

            return vec2(sa.x + sd * ux * sa.z, ea.x + ed * uy * ea.z);
        }

        vec2 g_unwarp(vec2 p) {
            vec4 sa = w0a;
            vec4 sb = w0b;
            float sd = -1.0;
            vec4 ea = w2a;
            vec4 eb = w2b;
            float ed = -1.0;
            int side = g_side_at(p.x);
            int end = g_end_at(p.y);
            if (mod(s6.w, 2.0) >= 1.0) {
                float x = p.x;
                float y = p.y;
                if (side > 0) {
                    g_warp(side, sa, sb, sd);
                    x = gw_to_canvas(sa, sb, sd, p.x);
                }

                if (end > 0) {
                    g_warp(end, ea, eb, ed);
                    y = gw_to_canvas(ea, eb, ed, p.y);
                }

                return vec2(x, y);
            }

            float cy = p.y;
            if (side > 0) {
                g_warp(side, sa, sb, sd);
                cy = sb.z + (p.y - sb.z) / g_row_fan(sb.z, gw_fan_screen(sa, sb, sd, p.x), gw_depth_screen(sa, sd, p.x));
                if (g_end_at(cy) == 0) {
                    return vec2(gw_to_canvas(sa, sb, sd, p.x), cy);
                }
            }

            float cx = p.x;
            if (end > 0) {
                g_warp(end, ea, eb, ed);
                float fan = g_column_fan(eb.z, gw_fan_screen(ea, eb, ed, p.y), gw_depth_screen(ea, ed, p.y));
                cx = eb.z + (p.x - eb.z) / fan;
                if (g_side_at(cx) == 0) {
                    return vec2(cx, gw_to_canvas(ea, eb, ed, p.y));
                }
            }

            if (side == 0) {
                side = g_side_at(cx);
            }

            if (end == 0) {
                end = g_end_at(cy);
            }

            if (side == 0 || end == 0) {
                float x = cx;
                float y = cy;
                if (side > 0) {
                    g_warp(side, sa, sb, sd);
                    x = gw_to_canvas(sa, sb, sd, p.x);
                }

                if (end > 0) {
                    g_warp(end, ea, eb, ed);
                    y = gw_to_canvas(ea, eb, ed, p.y);
                }

                return vec2(x, y);
            }

            g_warp(side, sa, sb, sd);
            g_warp(end, ea, eb, ed);
            return g_corner(sa, sb, sd, ea, eb, ed, p);
        }

        vec4 grid_glow(vec2 coord, vec2 size) {
            vec2 p = s8.yz + coord * vec2(s8.w, s9.x) / size;
            vec2 w = s0.yz + (p - s0.yz) / s0.x;
            vec2 c = g_unwarp(w);
            bool separable = mod(s6.w, 2.0) >= 1.0;
            bool desktop = mod(floor(s6.w / 2.0), 2.0) >= 1.0;
            bool seams = mod(floor(s6.w / 4.0), 2.0) >= 1.0;
            bool density = mod(floor(s6.w / 8.0), 2.0) >= 1.0;
            int side = g_side_at(c.x);
            int end = g_end_at(c.y);
            float sideScale = min(gw_scale(w0a, w0b, -1.0, c.x), gw_scale(w1a, w1b, 1.0, c.x));
            float endScale = min(gw_scale(w2a, w2b, -1.0, c.y), gw_scale(w3a, w3b, 1.0, c.y));
            vec4 a = w0a;
            vec4 b = w0b;
            float dir = -1.0;
            float rowFan = 1.0;
            bool shelf = false;
            float shelfScale = 1.0;
            if (side > 0) {
                g_warp(side, a, b, dir);
                if (!separable) {
                    rowFan = g_row_fan(b.z, gw_fan(a, b, dir, c.x), gw_depth(a, b, dir, c.x));
                }

                shelf = b.w > 0.5 && dir * (c.x - a.x) / a.z >= 1.0;
                shelfScale = a.w;
            }

            float columnFan = 1.0;
            if (end > 0) {
                g_warp(end, a, b, dir);
                if (!separable) {
                    columnFan = g_column_fan(b.z, gw_fan(a, b, dir, c.y), gw_depth(a, b, dir, c.y));
                }
            }

            float cell = s0.w;
            float strideX = side > 0 ? g_stride(sideScale) : 1.0;
            float strideY = end > 0 ? g_stride(endScale) : 1.0;
            if (shelf) {
                strideY = max(strideY, g_stride(shelfScale));
            }

            float stepX = cell * strideX;
            float stepY = cell * strideY;
            float column = floor(c.x / stepX + 0.5) * stepX;
            float row = floor(c.y / stepY + 0.5) * stepY;
            float unitX = s0.x * sideScale * columnFan;
            float unitY = s0.x * endScale * rowFan;
            bool flatX = gw_inside(w0a, -1.0, c.x) && gw_inside(w1a, 1.0, c.x);
            bool flatY = gw_inside(w2a, -1.0, c.y) && gw_inside(w3a, 1.0, c.y);
            bool hideColumn = !desktop && gw_inside(w0a, -1.0, column) && gw_inside(w1a, 1.0, column) && flatY;
            bool hideRow = !desktop && gw_inside(w2a, -1.0, row) && gw_inside(w3a, 1.0, row) && flatX;
            float big = 1000000.0;
            float dColumn = hideColumn ? big : max(0.0, abs((c.x - column) * unitX - 0.5) - 0.5);
            float dRow = hideRow ? big : max(0.0, abs((c.y - row) * unitY - 0.5) - 0.5);
            if (seams && !desktop) {
                float topSeam = gw_on(w2a) ? w2a.x : s8.z;
                float bottomSeam = gw_on(w3a) ? w3a.x : s8.z + s9.x;
                if (w.y >= topSeam && w.y <= bottomSeam) {
                    if (gw_on(w0a)) {
                        dColumn = min(dColumn, max(0.0, abs((w.x - (w0a.x - 1.0)) * s0.x - 0.5) - 0.5));
                    }

                    if (gw_on(w1a)) {
                        dColumn = min(dColumn, max(0.0, abs((w.x - w1a.x) * s0.x - 0.5) - 0.5));
                    }
                }
            }

            bool vertical = dColumn <= dRow;
            float d = min(dColumn, dRow);
            if (d >= big) {
                return vec4(0.0);
            }

            float spacingX = cell * strideX * unitX;
            float spacingY = cell * strideY * unitY;
            float spacing = side > 0 && end > 0 ? min(spacingX, spacingY) : vertical ? spacingX : spacingY;
            return g_glow(p, d, spacing, vertical, density);
        }
        """;

    private const string GlslMain = """

        vec4 basin_pixel(vec2 coord) {
            return grid_glow(coord, u_size);
        }
        """;

    private const string SkslMain = """

        half4 basin_pixel(float2 coord) {
            return half4(grid_glow(coord, u_size));
        }
        """;

    /// <summary>
    /// Writes the uniforms for a canvas grid from its warps, view, lights, effects and reveal.
    /// </summary>
    /// <param name="grid">The grid source to describe.</param>
    /// <param name="bounds">The bounds of the grid mesh, in output pixels.</param>
    /// <param name="into">The destination, with at least <see cref="UniformCount"/> values.</param>
    /// <exception cref="ArgumentException"><paramref name="into"/> is shorter than <see cref="UniformCount"/>.</exception>
    public static void Pack(CanvasGridSource grid, in Box bounds, Span<PixelShaderUniformValue> into)
    {
        if (into.Length < UniformCount)
        {
            throw new ArgumentException("The uniform span is shorter than the uniform list of the glow.", nameof(into));
        }

        Warp(grid.Left, into, 0);
        Warp(grid.Right, into, 2);
        Warp(grid.Top, into, 4);
        Warp(grid.Bottom, into, 6);
        var effects = grid.Effects;
        var flags = (grid.Separable ? 1 : 0) + (grid.DesktopLines ? 2 : 0) + (grid.SeamLines ? 4 : 0) + (effects.Density ? 8 : 0);
        GridGlowCommon.Pack(
            grid.Lights, effects, grid.Reveal, grid.Color, grid.Alpha, bounds, grid.MinLineSpacing, flags,
            new((float)grid.ViewScale, (float)grid.ViewCenterX, (float)grid.ViewCenterY, grid.CellSize),
            new((float)grid.CornerRadius, grid.CornerTaper ? 1f : 0f, GridGlowCommon.Far, GridGlowCommon.Far),
            into.Slice(8, GridGlowCommon.Lights), into.Slice(18, GridGlowCommon.Scalars));
    }

    private static void Warp(CanvasWarp? warp, Span<PixelShaderUniformValue> into, int at)
    {
        if (warp is not { IsIdentity: false } zone)
        {
            into[at] = default;
            into[at + 1] = default;
            return;
        }

        into[at] = new(zone.Seam, zone.ZoneWidth, zone.Extension, (float)zone.EdgeScale);
        into[at + 1] = new((float)zone.Exponent, (float)zone.Slope, (float)zone.Center, zone.Terrace ? 1f : 0f);
    }

    private static byte[] LoadSpirV()
    {
        using var stream = typeof(GridGlowShader).Assembly.GetManifestResourceStream("grid_glow.frag.spv")
            ?? throw new InvalidOperationException("The shader resource grid_glow.frag.spv is not embedded in the assembly.");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }
}
