using Basin.Capabilities;
using SkiaSharp;

namespace Basin.Frames.Metacity;

public sealed partial class MetacityPainter
{

    private MetacityFrameGeometry? _geometry;
    private MetacityFrameInput _input;
    private int _scale;
    private double _scaleFactor = 1;
    private int _titleWidth;
    private int _titleHeight;
    private bool _hasIcon;
    private SKTextBlob? _titleBlob;
    private SKFont? _titleFont;
    private SKColor[] _lut = [];

    private int Dev(int logical) => (int)Math.Round(logical * _scaleFactor);

    private Box DevBox(int x, int y, int width, int height) =>
        OutputScaling.ToPhysical(new Box(x, y, width, height), _scaleFactor);

    private int DevStroke(int logicalWidth) => Math.Max(1, (int)Math.Round(Math.Max(logicalWidth, 1) * _scaleFactor));

    private float LineCenter(int logical, bool halfOffset, int strokeWidth)
    {
        var center = (logical + (halfOffset ? 0.5 : 0.0)) * _scaleFactor;
        return strokeWidth % 2 == 0 ? (float)Math.Round(center) : (float)Math.Floor(center) + 0.5f;
    }

    public void Paint(SKCanvas canvas, in MetacityFrameInput input, MetacityFrameGeometry geometry, double scale)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(geometry);
        if (StyleFor(input) is not { } style)
        {
            return;
        }

        _geometry = geometry;
        _input = input;
        _scale = Math.Max(1, (int)Math.Ceiling(scale));
        _scaleFactor = scale > 0 ? scale : 1;
        _ = Colors;
        _lut = _colors;

        PrepareTitle(style.Layout, input);
        _hasIcon = Resources.HasIcon(input.State);

        var width = geometry.Width;
        var height = geometry.Height;
        var borders = geometry.Borders;
        var titlebar = new Box(0, 0, width, borders.Top);
        var leftTitlebarEdge = new Box(0, geometry.TopTitlebarEdge, geometry.LeftTitlebarEdge, borders.Top - geometry.TopTitlebarEdge - geometry.BottomTitlebarEdge);
        var rightTitlebarEdge = new Box(width - geometry.RightTitlebarEdge, leftTitlebarEdge.Y, geometry.RightTitlebarEdge, leftTitlebarEdge.Height);
        var topTitlebarEdge = new Box(0, 0, width, geometry.TopTitlebarEdge);
        var bottomTitlebarEdge = new Box(0, borders.Top - geometry.BottomTitlebarEdge, width, geometry.BottomTitlebarEdge);
        var leftEdge = new Box(0, borders.Top, borders.Left, height - borders.Top - borders.Bottom);
        var rightEdge = new Box(width - borders.Right, borders.Top, borders.Right, leftEdge.Height);
        var bottomEdge = new Box(0, height - borders.Bottom, width, borders.Bottom);
        var titlebarMiddle = new Box(
            leftTitlebarEdge.Right,
            topTitlebarEdge.Bottom,
            width - leftTitlebarEdge.Width - rightTitlebarEdge.Width,
            titlebar.Height - topTitlebarEdge.Height - bottomTitlebarEdge.Height);

        for (var piece = MetacityPiece.EntireBackground; piece < MetacityPiece.Count; piece++)
        {
            var rect = piece switch
            {
                MetacityPiece.EntireBackground or MetacityPiece.Overlay => new Box(0, 0, width, height),
                MetacityPiece.Titlebar => titlebar,
                MetacityPiece.LeftTitlebarEdge => leftTitlebarEdge,
                MetacityPiece.RightTitlebarEdge => rightTitlebarEdge,
                MetacityPiece.TopTitlebarEdge => topTitlebarEdge,
                MetacityPiece.BottomTitlebarEdge => bottomTitlebarEdge,
                MetacityPiece.TitlebarMiddle => titlebarMiddle,
                MetacityPiece.Title => geometry.TitleRect,
                MetacityPiece.LeftEdge => leftEdge,
                MetacityPiece.RightEdge => rightEdge,
                _ => bottomEdge,
            };

            if (piece == MetacityPiece.Overlay)
            {
                PaintButtons(canvas, style, geometry);
            }

            canvas.Save();
            canvas.ClipRect(Rect(rect));
            if (!canvas.IsClipEmpty && style.GetPiece(piece) is { } list)
            {
                DrawList(canvas, list, rect);
            }

            canvas.Restore();
        }

        ClearCorners(canvas, geometry);
        _geometry = null;
    }

    private void PaintButtons(SKCanvas canvas, MetacityFrameStyle style, MetacityFrameGeometry geometry)
    {
        var middleOffset = 0;
        var type = MetacityButtonType.LeftLeftBackground;
        while (type < MetacityButtonType.Count)
        {
            var rect = geometry.BackgroundRect(type, middleOffset);
            var state = ButtonState(type, geometry, middleOffset);
            if (state != MetacityButtonState.Count && style.GetButton(type, state) is { } list && !rect.IsEmpty)
            {
                canvas.Save();
                canvas.ClipRect(Rect(rect));
                if (!canvas.IsClipEmpty)
                {
                    DrawList(canvas, list, rect);
                }

                canvas.Restore();
            }

            if (type is MetacityButtonType.RightMiddleBackground or MetacityButtonType.LeftMiddleBackground &&
                middleOffset < MetacityFrameGeometry.MaxMiddleBackgrounds - 1)
            {
                middleOffset++;
            }
            else
            {
                middleOffset = 0;
                type++;
            }
        }
    }

    private MetacityButtonState ButtonState(MetacityButtonType type, MetacityFrameGeometry geometry, int middleOffset)
    {
        var function = MetacityButtonFunction.None;
        switch (type)
        {
            case MetacityButtonType.RightLeftBackground:
            case MetacityButtonType.RightSingleBackground:
                if (geometry.RightCount > 0)
                {
                    function = geometry.RightFunctions[0];
                }

                break;
            case MetacityButtonType.RightRightBackground:
                if (geometry.RightCount > 0)
                {
                    function = geometry.RightFunctions[geometry.RightCount - 1];
                }

                break;
            case MetacityButtonType.RightMiddleBackground:
                if (middleOffset + 1 < geometry.RightCount)
                {
                    function = geometry.RightFunctions[middleOffset + 1];
                }

                break;
            case MetacityButtonType.LeftLeftBackground:
            case MetacityButtonType.LeftSingleBackground:
                if (geometry.LeftCount > 0)
                {
                    function = geometry.LeftFunctions[0];
                }

                break;
            case MetacityButtonType.LeftRightBackground:
                if (geometry.LeftCount > 0)
                {
                    function = geometry.LeftFunctions[geometry.LeftCount - 1];
                }

                break;
            case MetacityButtonType.LeftMiddleBackground:
                if (middleOffset + 1 < geometry.LeftCount)
                {
                    function = geometry.LeftFunctions[middleOffset + 1];
                }

                break;
            default:
                return _input.ButtonState(type);
        }

        return function == MetacityButtonFunction.None
            ? MetacityButtonState.Count
            : _input.ButtonState(MetacityFrameGeometry.TypeOf(function));
    }

    private void PrepareTitle(MetacityFrameLayout layout, in MetacityFrameInput input)
    {
        _titleBlob = null;
        _titleFont = null;
        _titleWidth = 0;
        _titleHeight = 0;
        var title = input.State.Title is { Length: > 0 } text ? text : input.State.AppId;
        if (!layout.HasTitle || string.IsNullOrEmpty(title))
        {
            return;
        }

        var font = Font.FontFor(layout.TitleScale);
        if (Font.CacheFor(layout.TitleScale).TryGetBlob(title, font, out var blob, out var width))
        {
            _titleBlob = blob;
            _titleFont = font;
            _titleWidth = (int)Math.Ceiling(width);
            _titleHeight = Font.TextHeight(layout.TitleScale);
        }
    }

    private MetacityExpressionEnvironment Environment(in Box rect)
    {
        var geometry = _geometry!;
        var hasIcon = _hasIcon;
        return new MetacityExpressionEnvironment
        {
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            ObjectWidth = -1,
            ObjectHeight = -1,
            LeftWidth = geometry.Borders.Left,
            RightWidth = geometry.Borders.Right,
            TopHeight = geometry.Borders.Top,
            BottomHeight = geometry.Borders.Bottom,
            FrameXCenter = geometry.Width / 2 - rect.X,
            FrameYCenter = geometry.Height / 2 - rect.Y,
            MiniIconWidth = hasIcon ? MetacityResources.MiniIconSize : 0,
            MiniIconHeight = hasIcon ? MetacityResources.MiniIconSize : 0,
            IconWidth = hasIcon ? MetacityResources.IconSize : 0,
            IconHeight = hasIcon ? MetacityResources.IconSize : 0,
            TitleWidth = _titleWidth,
            TitleHeight = _titleHeight,
        };
    }

    private void DrawList(SKCanvas canvas, MetacityDrawOpList list, in Box rect)
    {
        if (list.Ops.Count == 0)
        {
            return;
        }

        var env = Environment(rect);
        canvas.Save();
        foreach (var op in list.Ops)
        {
            if (op.Kind == MetacityDrawOpKind.Clip)
            {
                canvas.Restore();
                canvas.ClipRect(new SKRect(
                    op.X!.Position(in env, true),
                    op.Y!.Position(in env, false),
                    op.X.Position(in env, true) + op.Width!.Size(in env),
                    op.Y.Position(in env, false) + op.Height!.Size(in env)));
                canvas.Save();
            }
            else if (!canvas.IsClipEmpty)
            {
                DrawOp(canvas, op, rect, ref env);
            }
        }

        canvas.Restore();
    }

    private static SKRect Rect(in Box box) => new(box.X, box.Y, box.Right, box.Bottom);

    private void DrawOp(SKCanvas canvas, MetacityDrawOp op, in Box rect, ref MetacityExpressionEnvironment env)
    {
        var fill = Resources.Fill;
        var stroke = Resources.Stroke;
        canvas.Save();
        try
        {
            switch (op.Kind)
            {
                case MetacityDrawOpKind.Line:
                    DrawLine(canvas, op, in env);
                    break;
                case MetacityDrawOpKind.Rectangle:
                {
                    var rx = op.X!.Position(in env, true);
                    var ry = op.Y!.Position(in env, false);
                    var rw = op.Width!.Size(in env);
                    var rh = op.Height!.Size(in env);
                    var dev = DevBox(rx, ry, rw, rh);
                    canvas.ResetMatrix();
                    if (op.Filled)
                    {
                        fill.Color = _lut[op.Color!.Index];
                        canvas.DrawRect(dev.X, dev.Y, dev.Width, dev.Height, fill);
                    }
                    else
                    {
                        var sw = DevStroke(1);
                        stroke.Color = _lut[op.Color!.Index];
                        stroke.StrokeWidth = sw;
                        stroke.PathEffect = null;
                        var left = LineCenter(rx, true, sw);
                        var top = LineCenter(ry, true, sw);
                        canvas.DrawRect(left, top, LineCenter(rx + rw, true, sw) - left, LineCenter(ry + rh, true, sw) - top, stroke);
                    }

                    break;
                }

                case MetacityDrawOpKind.Arc:
                    DrawArc(canvas, op, in env);
                    break;
                case MetacityDrawOpKind.Tint:
                {
                    var rx = op.X!.Position(in env, true);
                    var ry = op.Y!.Position(in env, false);
                    var rw = op.Width!.Size(in env);
                    var rh = op.Height!.Size(in env);
                    var color = _lut[op.Color!.Index];
                    var alpha = op.Alpha!;
                    var dev = DevBox(rx, ry, rw, rh);
                    canvas.ResetMatrix();
                    if (!alpha.NeedsAlpha)
                    {
                        fill.Color = color;
                        canvas.DrawRect(dev.X, dev.Y, dev.Width, dev.Height, fill);
                    }
                    else if (alpha.Alphas.Length == 1)
                    {
                        fill.Color = color.WithAlpha(alpha.Alphas[0]);
                        canvas.DrawRect(dev.X, dev.Y, dev.Width, dev.Height, fill);
                    }
                    else
                    {
                        fill.Color = SKColors.White;
                        fill.Shader = Resources.TintGradient(op, color, _paletteVersion);
                        canvas.Translate(dev.X, dev.Y);
                        canvas.Scale(dev.Width, dev.Height);
                        canvas.DrawRect(0, 0, 1, 1, fill);
                        fill.Shader = null;
                    }

                    break;
                }

                case MetacityDrawOpKind.Gradient:
                {
                    var rx = op.X!.Position(in env, true);
                    var ry = op.Y!.Position(in env, false);
                    var rw = op.Width!.Size(in env);
                    var rh = op.Height!.Size(in env);
                    var dev = DevBox(rx, ry, rw, rh);
                    canvas.ResetMatrix();
                    fill.Color = SKColors.White;
                    fill.Shader = Resources.Gradient(op, _lut, _paletteVersion);
                    canvas.Translate(dev.X, dev.Y);
                    canvas.Scale(dev.Width, dev.Height);
                    canvas.DrawRect(0, 0, 1, 1, fill);
                    fill.Shader = null;
                    break;
                }

                case MetacityDrawOpKind.Image:
                    DrawImage(canvas, op, ref env);
                    break;
                case MetacityDrawOpKind.GtkArrow:
                {
                    if (op.Arrow == MetacityArrow.None)
                    {
                        break;
                    }

                    var rx = op.X!.Position(in env, true);
                    var ry = op.Y!.Position(in env, false);
                    var size = Math.Max(op.Width!.Size(in env), op.Height!.Size(in env));
                    canvas.ResetMatrix();
                    fill.Color = SKColors.White;
                    canvas.DrawVertices(Resources.Arrow(op.Arrow, Dev(rx), Dev(ry), Dev(size), new SKColor(Palette.Fg(op.State).ToArgb32())), SKBlendMode.Modulate, fill);
                    break;
                }

                case MetacityDrawOpKind.GtkBox:
                {
                    var rx = op.X!.Position(in env, true);
                    var ry = op.Y!.Position(in env, false);
                    var rw = op.Width!.Size(in env);
                    var rh = op.Height!.Size(in env);
                    var dev = DevBox(rx, ry, rw, rh);
                    var sw = DevStroke(1);
                    canvas.ResetMatrix();
                    fill.Color = new SKColor(Palette.Bg(op.State).ToArgb32());
                    canvas.DrawRect(dev.X, dev.Y, dev.Width, dev.Height, fill);
                    stroke.Color = new SKColor(Palette.DarkOf(op.State).ToArgb32());
                    stroke.StrokeWidth = sw;
                    stroke.PathEffect = null;
                    var left = LineCenter(rx, true, sw);
                    var top = LineCenter(ry, true, sw);
                    canvas.DrawRect(left, top, LineCenter(rx + rw - 1, true, sw) - left, LineCenter(ry + rh - 1, true, sw) - top, stroke);
                    break;
                }

                case MetacityDrawOpKind.GtkVline:
                {
                    var rx = op.X!.Position(in env, true);
                    var ry1 = op.Y!.Position(in env, false);
                    var ry2 = op.Y2!.Position(in env, false);
                    var sw = DevStroke(1);
                    canvas.ResetMatrix();
                    stroke.Color = new SKColor(Palette.DarkOf(op.State).ToArgb32());
                    stroke.StrokeWidth = sw;
                    stroke.PathEffect = null;
                    var x = LineCenter(rx, true, sw);
                    canvas.DrawLine(x, Dev(ry1), x, Dev(ry2), stroke);
                    break;
                }

                case MetacityDrawOpKind.Icon:
                    DrawIcon(canvas, op, in env);
                    break;
                case MetacityDrawOpKind.Title:
                    DrawTitle(canvas, op, in env);
                    break;
                case MetacityDrawOpKind.OpList:
                {
                    var inner = new Box(
                        op.X!.Position(in env, true),
                        op.Y!.Position(in env, false),
                        op.Width!.Size(in env),
                        op.Height!.Size(in env));
                    DrawList(canvas, op.OpList!, inner);
                    break;
                }

                case MetacityDrawOpKind.Tile:
                {
                    var rx = op.X!.Position(in env, true);
                    var ry = op.Y!.Position(in env, false);
                    var rw = op.Width!.Size(in env);
                    var rh = op.Height!.Size(in env);
                    canvas.ClipRect(new SKRect(rx, ry, rx + rw, ry + rh));
                    var xOffset = op.TileXOffset!.Position(in env, true) - rect.X;
                    var yOffset = op.TileYOffset!.Position(in env, false) - rect.Y;
                    var tileWidth = op.TileWidth!.Size(in env);
                    var tileHeight = op.TileHeight!.Size(in env);
                    for (var tx = rx - xOffset; tx < rx + rw; tx += tileWidth)
                    {
                        for (var ty = ry - yOffset; ty < ry + rh; ty += tileHeight)
                        {
                            DrawList(canvas, op.OpList!, new Box(tx, ty, tileWidth, tileHeight));
                        }
                    }

                    break;
                }
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void DrawLine(SKCanvas canvas, MetacityDrawOp op, in MetacityExpressionEnvironment env)
    {
        var stroke = Resources.Stroke;
        stroke.Color = _lut[op.Color!.Index];
        var sw = DevStroke(op.LineWidth);
        stroke.StrokeWidth = sw;
        stroke.StrokeCap = SKStrokeCap.Butt;
        stroke.PathEffect = op.DashOn > 0 && op.DashOff > 0 ? Resources.Dash(op, _scaleFactor) : null;

        var x1 = op.X!.Position(in env, true);
        var y1 = op.Y!.Position(in env, false);
        canvas.ResetMatrix();
        if (op.X2 is null && op.Y2 is null && op.LineWidth == 0)
        {
            var fill = Resources.Fill;
            fill.Color = stroke.Color;
            var dot = DevBox(x1, y1, 1, 1);
            canvas.DrawRect(dot.X, dot.Y, dot.Width, dot.Height, fill);
            return;
        }

        var x2 = op.X2 is null ? x1 : op.X2.Position(in env, true);
        var y2 = op.Y2 is null ? y1 : op.Y2.Position(in env, false);
        if ((y1 == y2 || x1 == x2) && op.LineWidth != 0)
        {
            var odd = op.LineWidth % 2 != 0;
            if (y1 == y2)
            {
                var y = LineCenter(y1, odd, sw);
                canvas.DrawLine(Dev(x1), y, Dev(x2), y, stroke);
            }
            else
            {
                var x = LineCenter(x1, odd, sw);
                canvas.DrawLine(x, Dev(y1), x, Dev(y2), stroke);
            }
        }
        else
        {
            if (op.LineWidth == 0)
            {
                stroke.StrokeCap = SKStrokeCap.Square;
            }

            canvas.DrawLine(LineCenter(x1, true, sw), LineCenter(y1, true, sw), LineCenter(x2, true, sw), LineCenter(y2, true, sw), stroke);
        }

        stroke.PathEffect = null;
        stroke.StrokeCap = SKStrokeCap.Butt;
    }

    private void DrawArc(SKCanvas canvas, MetacityDrawOp op, in MetacityExpressionEnvironment env)
    {
        var rx = op.X!.Position(in env, true);
        var ry = op.Y!.Position(in env, false);
        var rw = op.Width!.Size(in env);
        var rh = op.Height!.Size(in env);
        var sw = DevStroke(1);
        var scale = (float)_scaleFactor;
        var centerX = (float)((rx + rw / 2.0 + 0.5) * scale);
        var centerY = (float)((ry + rh / 2.0 + 0.5) * scale);
        var halfWidth = rw * scale / 2f;
        var halfHeight = rh * scale / 2f;
        var oval = new SKRect(centerX - halfWidth, centerY - halfHeight, centerX + halfWidth, centerY + halfHeight);
        var start = (float)(op.StartAngle - 90.0);
        var sweep = (float)op.ExtentAngle;
        canvas.ResetMatrix();
        if (op.Filled)
        {
            var fill = Resources.Fill;
            fill.Color = _lut[op.Color!.Index];
            canvas.DrawArc(oval, start, sweep, true, fill);
        }
        else
        {
            var stroke = Resources.Stroke;
            stroke.Color = _lut[op.Color!.Index];
            stroke.StrokeWidth = sw;
            stroke.PathEffect = null;
            canvas.DrawArc(oval, start, sweep, false, stroke);
        }
    }

    private void DrawImage(SKCanvas canvas, MetacityDrawOp op, ref MetacityExpressionEnvironment env)
    {
        var info = op.Image!;
        env.ObjectWidth = info.Width;
        env.ObjectHeight = info.Height;
        var rw = op.Width!.Size(in env, true);
        var rh = op.Height!.Size(in env, true);
        var rx = op.X!.Position(in env, true, true);
        var ry = op.Y!.Position(in env, false, true);
        env.ObjectWidth = -1;
        env.ObjectHeight = -1;

        var dev = DevBox(rx, ry, rw, rh);
        if (dev.IsEmpty)
        {
            return;
        }

        var colorize = op.Colorize is { } spec ? (uint)_lut[spec.Index] : 0u;
        var repeating = op.FillType == MetacityFillType.Tile || info.HorizontalStripes || info.VerticalStripes;
        int decodeWidth;
        int decodeHeight;
        if (op.FillType == MetacityFillType.Tile)
        {
            decodeWidth = Math.Max(1, Dev(info.Width));
            decodeHeight = Math.Max(1, Dev(info.Height));
        }
        else if (info.HorizontalStripes && !info.VerticalStripes)
        {
            decodeWidth = info.Width;
            decodeHeight = dev.Height;
        }
        else if (info.VerticalStripes && !info.HorizontalStripes)
        {
            decodeWidth = dev.Width;
            decodeHeight = info.Height;
        }
        else
        {
            decodeWidth = dev.Width;
            decodeHeight = dev.Height;
        }

        var image = Resources.Image(info, decodeWidth, decodeHeight, colorize, out var repeat, repeating);
        if (image is null)
        {
            return;
        }

        DrawDeviceImage(canvas, image, repeat, op, dev);
    }

    private void DrawDeviceImage(SKCanvas canvas, SKImage image, SKShader? repeat, MetacityDrawOp op, in Box dev)
    {
        var alpha = op.Alpha;
        var fill = Resources.Fill;
        var masked = alpha is { NeedsAlpha: true } && alpha.Alphas.Length > 1;
        if (masked)
        {
            canvas.SaveLayer();
        }

        canvas.ResetMatrix();
        fill.Color = alpha is { NeedsAlpha: true, Alphas.Length: 1 } ? SKColors.White.WithAlpha(alpha.Alphas[0]) : SKColors.White;
        if (repeat is not null)
        {
            canvas.Save();
            canvas.Translate(dev.X, dev.Y);
            fill.Shader = repeat;
            canvas.DrawRect(0, 0, dev.Width, dev.Height, fill);
            fill.Shader = null;
            canvas.Restore();
        }
        else
        {
            canvas.DrawImage(image, new SKRect(dev.X, dev.Y, dev.Right, dev.Bottom), new SKSamplingOptions(SKFilterMode.Nearest), fill);
        }

        fill.Color = SKColors.White;
        if (masked)
        {
            fill.Shader = Resources.Mask(op);
            fill.BlendMode = SKBlendMode.DstIn;
            canvas.Translate(dev.X, dev.Y);
            canvas.Scale(dev.Width, dev.Height);
            canvas.DrawRect(0, 0, 1, 1, fill);
            fill.BlendMode = SKBlendMode.SrcOver;
            fill.Shader = null;
            canvas.Restore();
        }
    }

    private void DrawIcon(SKCanvas canvas, MetacityDrawOp op, in MetacityExpressionEnvironment env)
    {
        var rw = op.Width!.Size(in env);
        var rh = op.Height!.Size(in env);
        var rx = op.X!.Position(in env, true);
        var ry = op.Y!.Position(in env, false);
        var dev = DevBox(rx, ry, rw, rh);
        if (dev.IsEmpty)
        {
            return;
        }

        var image = Resources.Icon(_input.State, dev.Width, dev.Height, out var repeat, op.FillType == MetacityFillType.Tile);
        if (image is null)
        {
            return;
        }

        DrawDeviceImage(canvas, image, repeat, op, dev);
    }

    private void DrawTitle(SKCanvas canvas, MetacityDrawOp op, in MetacityExpressionEnvironment env)
    {
        if (_titleBlob is null || _titleFont is null)
        {
            return;
        }

        var fill = Resources.Fill;
        var color = _lut[op.Color!.Index];
        fill.Color = color;
        var rx = op.X!.Position(in env, true);
        var ry = op.Y!.Position(in env, false);
        var blob = _titleBlob;
        if (op.EllipsizeWidth is { } ellipsize)
        {
            var width = Math.Max(ellipsize.Evaluate(in env, false), 0);
            if (width < _titleWidth)
            {
                var title = _input.State.Title is { Length: > 0 } text ? text : _input.State.AppId!;
                if (!Font.CacheFor(StyleFor(_input)!.Layout.TitleScale).TryGetBlob(title, _titleFont, width, out blob, out _))
                {
                    return;
                }
            }
        }
        else if (rx - env.X + _titleWidth >= env.Width)
        {
            var textSpace = env.X + env.Width - (rx - env.X) - env.RightWidth;
            if (textSpace > 0)
            {
                fill.Shader = Resources.Fade(op, rx, ry, textSpace, env.TitleHeight, color);
            }
        }

        canvas.DrawText(blob, rx, ry - _titleFont.Metrics.Ascent, fill);
        fill.Shader = null;
    }

    private void ClearCorners(SKCanvas canvas, MetacityFrameGeometry geometry)
    {
        if (!geometry.HasRoundedCorner)
        {
            return;
        }

        var fill = Resources.Fill;
        fill.Color = SKColors.Transparent;
        fill.BlendMode = SKBlendMode.Clear;
        canvas.Save();
        canvas.ResetMatrix();
        var device = canvas.DeviceClipBounds;
        var width = device.Width;
        var height = device.Height;
        var scale = _scaleFactor;
        Corner(canvas, fill, (int)(geometry.TopLeftRadius * scale), 0, 0, 1, width);
        Corner(canvas, fill, (int)(geometry.TopRightRadius * scale), width, 0, 1, width);
        Corner(canvas, fill, (int)(geometry.BottomLeftRadius * scale), 0, height - 1, -1, width);
        Corner(canvas, fill, (int)(geometry.BottomRightRadius * scale), width, height - 1, -1, width);
        canvas.Restore();
        fill.BlendMode = SKBlendMode.SrcOver;
    }

    private static void Corner(SKCanvas canvas, SKPaint clear, int corner, int edgeX, int firstRow, int rowStep, int frameWidth)
    {
        if (corner <= 0)
        {
            return;
        }

        var r = Math.Sqrt(corner) + corner;
        for (var i = 0; i < corner; i++)
        {
            var rowWidth = (int)Math.Floor(0.5 + r - Math.Sqrt(r * r - (r - (i + 0.5)) * (r - (i + 0.5))));
            if (rowWidth <= 0)
            {
                continue;
            }

            var y = firstRow + i * rowStep;
            var cleared = edgeX == 0
                ? new SKRect(0, y, rowWidth, y + 1)
                : new SKRect(frameWidth - rowWidth, y, frameWidth, y + 1);
            canvas.DrawRect(cleared, clear);
        }
    }
}
