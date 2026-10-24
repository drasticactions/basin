using System.Drawing;
using Basin.Themes.MsStyles;

namespace Basin.Frames.MsStyles;

/// <summary>
/// A port of wine's <c>DrawThemeBackgroundEx</c>. It does not draw. It appends the operations for a theme part to an <see cref="MsStylesDrawList"/>.
/// </summary>
public static class MsStylesDrawing
{
    private static readonly ThemeProperty[] ImageFiles =
    [
        ThemeProperty.ImageFile1, ThemeProperty.ImageFile2, ThemeProperty.ImageFile3, ThemeProperty.ImageFile4,
        ThemeProperty.ImageFile5, ThemeProperty.ImageFile6, ThemeProperty.ImageFile7,
    ];

    private static readonly ThemeProperty[] MinDpis =
    [
        ThemeProperty.MinDpi1, ThemeProperty.MinDpi2, ThemeProperty.MinDpi3, ThemeProperty.MinDpi4,
        ThemeProperty.MinDpi5, ThemeProperty.MinDpi6, ThemeProperty.MinDpi7,
    ];

    private static readonly ThemeProperty[] MinSizes =
    [
        ThemeProperty.MinSize1, ThemeProperty.MinSize2, ThemeProperty.MinSize3, ThemeProperty.MinSize4,
        ThemeProperty.MinSize5, ThemeProperty.MinSize6, ThemeProperty.MinSize7,
    ];

    private static readonly ThemeProperty[] GradientColors =
    [
        ThemeProperty.GradientColor1, ThemeProperty.GradientColor2, ThemeProperty.GradientColor3,
        ThemeProperty.GradientColor4, ThemeProperty.GradientColor5,
    ];

    private static readonly ThemeProperty[] GradientRatios =
    [
        ThemeProperty.GradientRatio1, ThemeProperty.GradientRatio2, ThemeProperty.GradientRatio3,
        ThemeProperty.GradientRatio4, ThemeProperty.GradientRatio5,
    ];

    /// <summary>
    /// Appends the background of a part, image or border fill, and then its glyph. All rectangles are device pixels.
    /// </summary>
    /// <param name="list">The list to append to.</param>
    /// <param name="cls">The theme class.</param>
    /// <param name="part">The part number.</param>
    /// <param name="state">The state number.</param>
    /// <param name="destination">The area to draw in device pixels.</param>
    /// <param name="context">The image cache, output DPI and opacity.</param>
    /// <param name="frame">The 1-based image frame to draw, or 0 to use the state number.</param>
    /// <returns><see langword="true"/> when the background drew. It is false when the type is none, the area is empty or the image did not load.</returns>
    public static bool Background(MsStylesDrawList list, MsStylesClass cls, int part, int state, RectangleF destination, in MsStylesDrawContext context, int frame = 0)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(cls);
        var background = cls.TryGetEnum<BackgroundType>(part, state, ThemeProperty.BgType, out var type) ? type : BackgroundType.BorderFill;
        if (background == BackgroundType.None || destination.Width <= 0 || destination.Height <= 0)
        {
            return false;
        }

        var drawn = background == BackgroundType.ImageFile
            ? ImageBackground(list, cls, part, state, destination, context, frame)
            : BorderBackground(list, cls, part, state, destination, context);
        Glyph(list, cls, part, state, ContentRect(cls, part, state, destination, context.Dpi), context, frame);
        return drawn;
    }

    /// <summary>
    /// Computes the content area of a part from its <c>CONTENTMARGINS</c>. Without them it uses the border size of a border fill, or the sizing margins of an image.
    /// </summary>
    /// <param name="cls">The theme class.</param>
    /// <param name="part">The part number.</param>
    /// <param name="state">The state number.</param>
    /// <param name="bounds">The part area in device pixels.</param>
    /// <param name="dpi">The output DPI that scales the margins.</param>
    /// <returns>The content area in device pixels.</returns>
    public static RectangleF ContentRect(MsStylesClass cls, int part, int state, RectangleF bounds, double dpi)
    {
        ArgumentNullException.ThrowIfNull(cls);
        var scale = (float)(dpi / 96.0);
        if (cls.TryGetMargins(part, state, ThemeProperty.ContentMargins, out var content))
        {
            return Deflate(bounds, content, scale);
        }

        var background = cls.TryGetEnum<BackgroundType>(part, state, ThemeProperty.BgType, out var type) ? type : BackgroundType.BorderFill;
        if (background == BackgroundType.BorderFill)
        {
            var size = cls.TryGetInt(part, state, ThemeProperty.BorderSize, out var border) ? border : 1;
            var inset = MathF.Round(size * scale);
            return RectangleF.FromLTRB(bounds.Left + inset, bounds.Top + inset, bounds.Right - inset, bounds.Bottom - inset);
        }

        if (background == BackgroundType.ImageFile && cls.TryGetMargins(part, state, ThemeProperty.SizingMargins, out var sizing))
        {
            return Deflate(bounds, sizing, scale);
        }

        return bounds;
    }

    /// <summary>
    /// Picks the image for a part by its <c>IMAGESELECTTYPE</c>: by DPI, by size, or the single image file.
    /// </summary>
    /// <param name="cls">The theme class.</param>
    /// <param name="part">The part number.</param>
    /// <param name="state">The state number.</param>
    /// <param name="destination">The area to draw in device pixels. Selection by size uses it.</param>
    /// <param name="glyph">Whether to pick the glyph image instead of the background.</param>
    /// <param name="context">The image cache and output DPI.</param>
    /// <param name="image">The picked image when the call returns <see langword="true"/>.</param>
    /// <param name="imageDpi">The DPI that the picked image is drawn for. It is 96 by default.</param>
    /// <returns><see langword="true"/> when the part has an image to draw.</returns>
    public static bool TrySelectImage(MsStylesClass cls, int part, int state, RectangleF destination, bool glyph, in MsStylesDrawContext context, out MsStylesImage image, out double imageDpi)
    {
        ArgumentNullException.ThrowIfNull(cls);
        imageDpi = 96;
        if (!glyph &&
            cls.TryGetEnum<GlyphType>(part, state, ThemeProperty.GlyphType, out var glyphType) && glyphType == GlyphType.ImageGlyph &&
            cls.TryGetImage(part, state, ThemeProperty.ImageFile, out image))
        {
            return true;
        }

        var select = cls.TryGetEnum<ImageSelectType>(part, state, ThemeProperty.ImageSelectType, out var selectType) ? selectType : ImageSelectType.None;
        if (select == ImageSelectType.Dpi)
        {
            for (var i = MinDpis.Length - 1; i >= 0; i--)
            {
                if (cls.TryGetInt(part, state, MinDpis[i], out var required) && required != 0 && context.Dpi >= required)
                {
                    imageDpi = required;
                    if (cls.TryGetImage(part, state, ImageFiles[i], out image))
                    {
                        return true;
                    }

                    imageDpi = 96;
                    break;
                }
            }

            if (cls.TryGetImage(part, state, ThemeProperty.ImageFile1, out image))
            {
                return true;
            }
        }
        else if (select == ImageSelectType.Size)
        {
            for (var i = ImageFiles.Length - 1; i >= 0; i--)
            {
                if (!cls.TryGetImage(part, state, ImageFiles[i], out var candidate))
                {
                    continue;
                }

                int width;
                int height;
                if (cls.TryGetPosition(part, state, MinSizes[i], out var minimum))
                {
                    width = minimum.X;
                    height = minimum.Y;
                }
                else if (context.Images.Get(candidate) is { } bitmap)
                {
                    FrameSize(cls, part, state, bitmap.Width, bitmap.Height, out width, out height);
                }
                else
                {
                    continue;
                }

                if (width <= destination.Width + 0.001f && height <= destination.Height + 0.001f)
                {
                    image = candidate;
                    imageDpi = context.Dpi;
                    return true;
                }
            }

            if (cls.TryGetImage(part, state, ThemeProperty.ImageFile1, out image))
            {
                imageDpi = context.Dpi;
                return true;
            }
        }

        return cls.TryGetImage(part, state, glyph ? ThemeProperty.GlyphImageFile : ThemeProperty.ImageFile, out image);
    }

    /// <summary>
    /// Picks and decodes the image of a part, applies its transparency key, and finds the source cell for a frame. It reads the DWM atlas when the part has an atlas rectangle.
    /// </summary>
    /// <param name="cls">The theme class.</param>
    /// <param name="part">The part number.</param>
    /// <param name="state">The state number.</param>
    /// <param name="destination">The area to draw in device pixels.</param>
    /// <param name="glyph">Whether to load the glyph image instead of the background.</param>
    /// <param name="context">The image cache and output DPI.</param>
    /// <param name="frame">The 1-based image frame, or 0 to use the state number.</param>
    /// <param name="bitmap">The decoded bitmap when the call returns <see langword="true"/>.</param>
    /// <param name="source">The source cell in texels.</param>
    /// <param name="imageDpi">The DPI that the image is drawn for.</param>
    /// <returns><see langword="true"/> when the image loaded and the cell is not empty.</returns>
    public static bool TryLoadImage(MsStylesClass cls, int part, int state, RectangleF destination, bool glyph, in MsStylesDrawContext context, int frame, out MsStylesBitmap bitmap, out Rectangle source, out double imageDpi)
    {
        ArgumentNullException.ThrowIfNull(cls);
        bitmap = null!;
        source = default;
        imageDpi = 96;
        MsStylesImage image;
        Rectangle region;
        if (!glyph && cls.TryGetRect(part, state, ThemeProperty.AtlasRect, out var atlasRect) &&
            cls.TryGetImage(part, state, ThemeProperty.AtlasImage, out image))
        {
            if (context.Images.Get(image) is not { } atlas)
            {
                return false;
            }

            bitmap = atlas;
            region = Rectangle.FromLTRB(atlasRect.Left, atlasRect.Top, atlasRect.Right, atlasRect.Bottom);
        }
        else
        {
            if (!TrySelectImage(cls, part, state, destination, glyph, context, out image, out imageDpi) ||
                context.Images.Get(image) is not { } selected)
            {
                return false;
            }

            bitmap = selected;
            region = new Rectangle(0, 0, selected.Width, selected.Height);
        }

        var hasKey = !bitmap.HasAlpha &&
                     cls.TryGetBool(part, state, glyph ? ThemeProperty.GlyphTransparent : ThemeProperty.Transparent, out var transparent) &&
                     transparent;
        if (hasKey)
        {
            var key = cls.TryGetColor(part, state, glyph ? ThemeProperty.GlyphTransparentColor : ThemeProperty.TransparentColor, out var keyColor)
                ? keyColor.ToColorRef()
                : MsStylesImageCache.DefaultKey;
            if (context.Images.Get(image, key) is { } keyed)
            {
                bitmap = keyed;
            }
        }

        var count = cls.TryGetInt(part, state, ThemeProperty.ImageCount, out var imageCount) ? Math.Max(imageCount, 1) : 1;
        var layout = cls.TryGetEnum<ImageLayout>(part, state, ThemeProperty.ImageLayout, out var imageLayout) ? imageLayout : ImageLayout.Horizontal;
        var index = Math.Max(Math.Min(count, frame > 0 ? frame : state), 1) - 1;
        if (layout == ImageLayout.Vertical)
        {
            var height = region.Height / count;
            source = new Rectangle(region.X, region.Y + index * height, region.Width, height);
        }
        else
        {
            var width = region.Width / count;
            source = new Rectangle(region.X + index * width, region.Y, width, region.Height);
        }

        return source.Width > 0 && source.Height > 0;
    }

    /// <summary>
    /// Measures the size of one image cell of a part, scaled from the image DPI to the output DPI.
    /// </summary>
    /// <param name="cls">The theme class.</param>
    /// <param name="part">The part number.</param>
    /// <param name="state">The state number.</param>
    /// <param name="context">The image cache and output DPI.</param>
    /// <param name="size">The size in device pixels when the call returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the part has an image that loads.</returns>
    public static bool TryPartSize(MsStylesClass cls, int part, int state, in MsStylesDrawContext context, out SizeF size)
    {
        var probe = new RectangleF(0, 0, 4096, 4096);
        if (TryLoadImage(cls, part, state, probe, glyph: false, context, 0, out _, out var source, out var imageDpi))
        {
            var k = (float)(context.Dpi / imageDpi);
            size = new SizeF(source.Width * k, source.Height * k);
            return true;
        }

        size = default;
        return false;
    }

    private static void FrameSize(MsStylesClass cls, int part, int state, int width, int height, out int frameWidth, out int frameHeight)
    {
        var count = cls.TryGetInt(part, state, ThemeProperty.ImageCount, out var imageCount) ? Math.Max(imageCount, 1) : 1;
        var layout = cls.TryGetEnum<ImageLayout>(part, state, ThemeProperty.ImageLayout, out var imageLayout) ? imageLayout : ImageLayout.Horizontal;
        frameWidth = layout == ImageLayout.Vertical ? width : width / count;
        frameHeight = layout == ImageLayout.Vertical ? height / count : height;
    }

    private static bool ImageBackground(MsStylesDrawList list, MsStylesClass cls, int part, int state, RectangleF destination, in MsStylesDrawContext context, int frame)
    {
        if (!TryLoadImage(cls, part, state, destination, glyph: false, context, frame, out var bitmap, out var source, out var imageDpi))
        {
            return false;
        }

        var k = (float)(context.Dpi / imageDpi);
        var opacity = context.Opacity;
        var sizing = cls.TryGetEnum<SizingType>(part, state, ThemeProperty.SizingType, out var sizingType) ? sizingType : SizingType.Stretch;
        var left = MathF.Round(destination.Left);
        var top = MathF.Round(destination.Top);
        var right = MathF.Round(destination.Right);
        var bottom = MathF.Round(destination.Bottom);
        var dstW = right - left;
        var dstH = bottom - top;
        if (sizing == SizingType.TrueSize)
        {
            var draw = new SizeF(source.Width * k, source.Height * k);
            var mark = cls.TryGetInt(part, state, ThemeProperty.TrueSizeStretchMark, out var stretchMark) ? stretchMark : 0;
            var scaling = cls.TryGetEnum<TrueSizeScalingType>(part, state, ThemeProperty.TrueSizeScalingType, out var scalingType) ? scalingType : TrueSizeScalingType.None;
            var wanted = new SizeF(dstW, dstH);
            if (cls.TryGetBool(part, state, ThemeProperty.UniformSizing, out var uniform) && uniform)
            {
                wanted = wanted.Width * draw.Height < wanted.Height * draw.Width
                    ? new SizeF(wanted.Width, draw.Height * wanted.Width / draw.Width)
                    : new SizeF(draw.Width * wanted.Height / draw.Height, wanted.Height);
            }

            if ((wanted.Width < draw.Width && wanted.Height < draw.Height) ||
                (scaling == TrueSizeScalingType.Size && wanted.Width * 100 / draw.Width >= mark + 100 && wanted.Height * 100 / draw.Height >= mark + 100))
            {
                draw = wanted;
            }

            var halign = cls.TryGetEnum<HorizontalAlign>(part, state, ThemeProperty.HAlign, out var h) ? h : HorizontalAlign.Center;
            var valign = cls.TryGetEnum<VerticalAlign>(part, state, ThemeProperty.VAlign, out var v) ? v : VerticalAlign.Center;
            var x = halign switch
            {
                HorizontalAlign.Center => left + MathF.Floor((dstW - draw.Width) / 2),
                HorizontalAlign.Right => right - draw.Width,
                _ => left,
            };
            var y = valign switch
            {
                VerticalAlign.Center => top + MathF.Floor((dstH - draw.Height) / 2),
                VerticalAlign.Bottom => bottom - draw.Height,
                _ => top,
            };
            list.Image(bitmap.Handle, source, source, new RectangleF(x, y, MathF.Round(draw.Width), MathF.Round(draw.Height)), opacity, tileScale: k);
            return true;
        }

        var margins = cls.TryGetMargins(part, state, ThemeProperty.SizingMargins, out var sizingMargins) ? sizingMargins : default;
        var logical = (float)(context.Dpi / 96.0);
        var ml = MathF.Round(margins.Left * logical);
        var mr = MathF.Round(margins.Right * logical);
        var mt = MathF.Round(margins.Top * logical);
        var mb = MathF.Round(margins.Bottom * logical);
        var srcW = source.Width * k;
        var srcH = source.Height * k;
        if (mt + mb > dstH)
        {
            mt = MathF.Round(mt * dstH / srcH);
            mb = dstH - mt;
        }

        if (ml + mr > dstW)
        {
            ml = MathF.Round(ml * dstW / srcW);
            mr = dstW - ml;
        }

        var sl = ml / k;
        var sr = mr / k;
        var st = mt / k;
        var sb = mb / k;
        var sx = (float)source.X;
        var sy = (float)source.Y;
        var sw = (float)source.Width;
        var sh = (float)source.Height;
        var tile = sizing == SizingType.Tile;

        list.Image(bitmap.Handle, source, new RectangleF(sx, sy, sl, st), new RectangleF(left, top, ml, mt), opacity, tileScale: k);
        list.Image(bitmap.Handle, source, new RectangleF(sx + sw - sr, sy, sr, st), new RectangleF(right - mr, top, mr, mt), opacity, tileScale: k);
        list.Image(bitmap.Handle, source, new RectangleF(sx, sy + sh - sb, sl, sb), new RectangleF(left, bottom - mb, ml, mb), opacity, tileScale: k);
        list.Image(bitmap.Handle, source, new RectangleF(sx + sw - sr, sy + sh - sb, sr, sb), new RectangleF(right - mr, bottom - mb, mr, mb), opacity, tileScale: k);

        var centerW = dstW - ml - mr;
        var centerH = dstH - mt - mb;
        var srcCenterW = sw - sl - sr;
        var srcCenterH = sh - st - sb;
        if (centerW > 0)
        {
            list.Image(bitmap.Handle, source, new RectangleF(sx + sl, sy, srcCenterW, st), new RectangleF(left + ml, top, centerW, mt), opacity, tile, tile, k);
            list.Image(bitmap.Handle, source, new RectangleF(sx + sl, sy + sh - sb, srcCenterW, sb), new RectangleF(left + ml, bottom - mb, centerW, mb), opacity, tile, tile, k);
        }

        if (centerH > 0)
        {
            list.Image(bitmap.Handle, source, new RectangleF(sx, sy + st, sl, srcCenterH), new RectangleF(left, top + mt, ml, centerH), opacity, tile, tile, k);
            list.Image(bitmap.Handle, source, new RectangleF(sx + sw - sr, sy + st, sr, srcCenterH), new RectangleF(right - mr, top + mt, mr, centerH), opacity, tile, tile, k);
        }

        if (centerW > 0 && centerH > 0 &&
            !(cls.TryGetBool(part, state, ThemeProperty.BorderOnly, out var borderOnly) && borderOnly))
        {
            list.Image(bitmap.Handle, source, new RectangleF(sx + sl, sy + st, srcCenterW, srcCenterH), new RectangleF(left + ml, top + mt, centerW, centerH), opacity, tile, tile, k);
        }

        return true;
    }

    private static bool BorderBackground(MsStylesDrawList list, MsStylesClass cls, int part, int state, RectangleF destination, in MsStylesDrawContext context)
    {
        var scale = (float)context.Scale;
        var shape = cls.TryGetEnum<BorderType>(part, state, ThemeProperty.BorderType, out var borderType) ? borderType : BorderType.Rect;
        var radius = shape == BorderType.RoundRect && cls.TryGetInt(part, state, ThemeProperty.RoundCornerWidth, out var cornerWidth)
            ? cornerWidth * scale / 2
            : 0;
        var content = destination;
        var size = cls.TryGetInt(part, state, ThemeProperty.BorderSize, out var borderSize) ? borderSize : 1;
        if (size > 0)
        {
            var device = MathF.Max(1, MathF.Round(size * scale));
            var color = cls.TryGetColor(part, state, ThemeProperty.BorderColor, out var borderColor) ? borderColor.ToArgb() : 0xff000000;
            list.Border(destination, device, color, shape, radius);
            content = RectangleF.FromLTRB(destination.Left + device, destination.Top + device, destination.Right - device, destination.Bottom - device);
        }

        var fill = cls.TryGetEnum<FillType>(part, state, ThemeProperty.FillType, out var fillType) ? fillType : FillType.Solid;
        switch (fill)
        {
            case FillType.Solid:
                if (shape == BorderType.Rect)
                {
                    list.Fill(content, cls.TryGetColor(part, state, ThemeProperty.FillColor, out var fillColor) ? fillColor.ToArgb() : 0xffffffff);
                }
                else
                {
                    var solid = default(MsStylesGradient);
                    solid.Kind = FillType.Solid;
                    solid.Count = 1;
                    solid.Set(0, cls.TryGetColor(part, state, ThemeProperty.FillColor, out var shapedColor) ? shapedColor.ToArgb() : 0xffffffff, 0);
                    list.Gradient(content, solid, shape, MathF.Max(0, radius - 1));
                }

                break;
            case FillType.VerticalGradient:
            case FillType.HorizontalGradient:
            case FillType.RadialGradient:
                var gradient = default(MsStylesGradient);
                gradient.Kind = fill;
                for (var i = 0; i < GradientColors.Length; i++)
                {
                    if (!cls.TryGetColor(part, state, GradientColors[i], out var stop))
                    {
                        if (i < 2)
                        {
                            stop = i == 0 ? new MsStylesColor(0, 0, 0) : new MsStylesColor(255, 255, 255);
                        }
                        else
                        {
                            break;
                        }
                    }

                    var ratio = cls.TryGetInt(part, state, GradientRatios[i], out var r) ? r : -1;
                    gradient.Set(i, stop.ToArgb(), ratio);
                    gradient.Count = i + 1;
                }

                list.Gradient(content, gradient, shape, MathF.Max(0, radius - 1));
                break;
            case FillType.TileImage:
                cls.Theme.Diagnostics.AddOnce("unimplemented", "FillType=TileImage");
                break;
        }

        return true;
    }

    private static void Glyph(MsStylesDrawList list, MsStylesClass cls, int part, int state, RectangleF content, in MsStylesDrawContext context, int frame)
    {
        if (!cls.TryGetEnum<GlyphType>(part, state, ThemeProperty.GlyphType, out var glyphType) || glyphType == GlyphType.None)
        {
            return;
        }

        if (glyphType == GlyphType.FontGlyph)
        {
            if (cls.TryGetFont(part, state, ThemeProperty.GlyphFont, out var font) &&
                cls.TryGetInt(part, state, ThemeProperty.GlyphIndex, out var index) && index > 0 && index <= char.MaxValue)
            {
                var color = cls.TryGetColor(part, state, ThemeProperty.GlyphTextColor, out var glyphColor) ? glyphColor.ToArgb() : 0xff000000;
                list.Text(GlyphText(index), font, (float)font.PixelSize(context.Dpi), content, HorizontalAlign.Center, color);
            }

            return;
        }

        var left = MathF.Round(content.Left);
        var top = MathF.Round(content.Top);
        var rounded = RectangleF.FromLTRB(left, top, MathF.Round(content.Right), MathF.Round(content.Bottom));
        if (!TryLoadImage(cls, part, state, rounded, glyph: true, context, frame, out var bitmap, out var source, out var imageDpi))
        {
            return;
        }

        var k = (float)(context.Dpi / imageDpi);
        var width = MathF.Round(source.Width * k);
        var height = MathF.Round(source.Height * k);
        var halign = cls.TryGetEnum<HorizontalAlign>(part, state, ThemeProperty.HAlign, out var h) ? h : HorizontalAlign.Center;
        var valign = cls.TryGetEnum<VerticalAlign>(part, state, ThemeProperty.VAlign, out var v) ? v : VerticalAlign.Center;
        var x = halign switch
        {
            HorizontalAlign.Center => left + MathF.Floor((rounded.Width - width) / 2),
            HorizontalAlign.Right => rounded.Right - width,
            _ => left,
        };
        var y = valign switch
        {
            VerticalAlign.Center => top + MathF.Floor((rounded.Height - height) / 2),
            VerticalAlign.Bottom => rounded.Bottom - height,
            _ => top,
        };
        list.Image(bitmap.Handle, source, source, new RectangleF(x, y, width, height), context.Opacity, tileScale: k);
    }

    private static readonly string[] GlyphTexts = new string[256];

    private static string GlyphText(int index)
    {
        if (index < GlyphTexts.Length)
        {
            return GlyphTexts[index] ??= ((char)index).ToString();
        }

        return ((char)index).ToString();
    }

    private static RectangleF Deflate(RectangleF bounds, MsStylesMargins margins, float scale) =>
        RectangleF.FromLTRB(
            bounds.Left + MathF.Round(margins.Left * scale),
            bounds.Top + MathF.Round(margins.Top * scale),
            bounds.Right - MathF.Round(margins.Right * scale),
            bounds.Bottom - MathF.Round(margins.Bottom * scale));
}
