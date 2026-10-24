using SkiaSharp;
using Basin.Themes.MsStyles;

namespace Basin.Frames.MsStyles;

/// <summary>
/// Replays an <see cref="MsStylesDrawList"/> on a Skia canvas.
/// </summary>
public static class MsStylesSkiaPainter
{
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest);
    private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear);

    /// <summary>
    /// Draws every operation of a draw list. The canvas must map one unit to one device pixel.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="list">The operations to draw.</param>
    /// <param name="resources">The Skia objects for the images, shaders, icons and text.</param>
    public static void Paint(SKCanvas canvas, MsStylesDrawList list, MsStylesResources resources)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(resources);
        foreach (ref readonly var op in list.Ops)
        {
            switch (op.Kind)
            {
                case MsStylesDrawOpKind.Image:
                    DrawImage(canvas, op, resources);
                    break;
                case MsStylesDrawOpKind.Fill:
                    resources.FillPaint.Shader = null;
                    resources.FillPaint.Color = new SKColor(op.Color);
                    canvas.DrawRect(Rect(op.Destination), resources.FillPaint);
                    break;
                case MsStylesDrawOpKind.Border:
                    DrawBorder(canvas, op, resources);
                    break;
                case MsStylesDrawOpKind.Gradient:
                    DrawGradient(canvas, op, resources);
                    break;
                case MsStylesDrawOpKind.Text:
                case MsStylesDrawOpKind.Glow:
                    resources.Text?.Draw(canvas, op);
                    break;
                case MsStylesDrawOpKind.Icon:
                    if (resources.IconFor(op.Icon, op.Text, (int)op.Destination.Width) is { } icon)
                    {
                        resources.ImagePaint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(MathF.Round(op.Opacity * 255), 0, 255));
                        canvas.DrawImage(icon, Rect(op.Destination), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), resources.ImagePaint);
                    }

                    break;
                case MsStylesDrawOpKind.Clip:
                    canvas.Save();
                    canvas.ClipRect(Rect(op.Destination));
                    break;
                case MsStylesDrawOpKind.Restore:
                    canvas.Restore();
                    break;
            }
        }
    }

    private static void DrawImage(SKCanvas canvas, in MsStylesDrawOp op, MsStylesResources resources)
    {
        if (resources.Frame(op.Bitmap, op.Frame) is not { } image)
        {
            return;
        }

        var paint = resources.ImagePaint;
        paint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(MathF.Round(op.Opacity * 255), 0, 255));
        var sampling = op.Smooth ? Linear : Nearest;
        var source = new SKRect(op.Source.Left - op.Frame.X, op.Source.Top - op.Frame.Y, op.Source.Right - op.Frame.X, op.Source.Bottom - op.Frame.Y);
        var destination = Rect(op.Destination);
        if (!op.TileX && !op.TileY)
        {
            canvas.DrawImage(image, source, destination, sampling, paint);
            return;
        }

        var tileWidth = op.Source.Width * op.TileScale;
        var tileHeight = op.Source.Height * op.TileScale;
        if (tileWidth < 0.5f || tileHeight < 0.5f)
        {
            return;
        }

        canvas.Save();
        canvas.ClipRect(destination);
        for (var y = destination.Top; y < destination.Bottom; y += tileHeight)
        {
            for (var x = destination.Left; x < destination.Right; x += tileWidth)
            {
                canvas.DrawImage(image, source, new SKRect(x, y, x + tileWidth, y + tileHeight), sampling, paint);
            }
        }

        canvas.Restore();
    }

    private static void DrawBorder(SKCanvas canvas, in MsStylesDrawOp op, MsStylesResources resources)
    {
        var paint = resources.StrokePaint;
        paint.Color = new SKColor(op.Color);
        paint.StrokeWidth = op.Size;
        var half = op.Size / 2;
        var rect = new SKRect(op.Destination.Left + half, op.Destination.Top + half, op.Destination.Right - half, op.Destination.Bottom - half);
        switch (op.Shape)
        {
            case BorderType.Ellipse:
                canvas.DrawOval(rect, paint);
                break;
            case BorderType.RoundRect:
                canvas.DrawRoundRect(rect, op.Radius, op.Radius, paint);
                break;
            default:
                paint.IsAntialias = false;
                canvas.DrawRect(rect, paint);
                paint.IsAntialias = true;
                break;
        }
    }

    private static void DrawGradient(SKCanvas canvas, in MsStylesDrawOp op, MsStylesResources resources)
    {
        var paint = resources.FillPaint;
        var rect = Rect(op.Destination);
        if (op.Gradient.Kind == FillType.Solid)
        {
            paint.Shader = null;
            paint.Color = new SKColor(op.Gradient.Color0);
        }
        else
        {
            paint.Color = SKColors.Black;
            paint.Shader = resources.Gradient(op.Gradient);
        }

        canvas.Save();
        canvas.Translate(rect.Left, rect.Top);
        canvas.Scale(rect.Width, rect.Height);
        var unit = new SKRect(0, 0, 1, 1);
        switch (op.Shape)
        {
            case BorderType.Ellipse:
                canvas.DrawOval(unit, paint);
                break;
            case BorderType.RoundRect:
                canvas.DrawRoundRect(unit, op.Radius / rect.Width, op.Radius / rect.Height, paint);
                break;
            default:
                canvas.DrawRect(unit, paint);
                break;
        }

        canvas.Restore();
        paint.Shader = null;
    }

    private static SKRect Rect(System.Drawing.RectangleF rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
