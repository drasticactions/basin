using System.Drawing;
using Basin.Capabilities;
using Basin.Themes.MsStyles;

namespace Basin.Frames.MsStyles;

/// <content>
/// The glass look: the DWM composition of a Vista frame, its button hover motion and its backdrop region.
/// </content>
public sealed partial class MsStylesFramePainter
{
    private static readonly int[] CornerInsets = [5, 3, 2, 1];

    private float _glassScale;
    private MsStylesDrawList? _glassList;

    private void LayoutGlassButtons(in FrameState state, MsStylesFrameGeometry geometry, float x0, float y0, float right, bool small)
    {
        var count = 0;
        if (small)
        {
            var size = VistaButtonMetrics.GlassToolSize;
            ref var close = ref geometry.Button(count++);
            close.Part = FramePart.Close;
            close.WindowPart = DwmPart.SmallCloseButton;
            close.Bounds = new RectangleF(right - VistaButtonMetrics.GlassToolRight - size, y0 + VistaButtonMetrics.GlassToolTop, size, size);
            close.Enabled = true;
            geometry.ButtonCount = count;
            return;
        }

        var maximized = state.Maximized;
        var top = maximized ? y0 + VistaButtonMetrics.GlassMaximizedTop : y0 + VistaButtonMetrics.GlassTop;
        var x = right - (maximized ? VistaButtonMetrics.GlassMaximizedRight : VistaButtonMetrics.GlassRight);
        var height = VistaButtonMetrics.GlassHeight;
        var minimize = (state.Capabilities & FrameCapabilities.Minimize) != 0;
        var maximize = (state.Capabilities & FrameCapabilities.Maximize) != 0;
        var row = state.Kind == FrameKind.Normal && (minimize || maximize);

        void Add(FramePart part, int dwmPart, int width, bool enabled)
        {
            x -= width;
            ref var button = ref geometry.Button(count++);
            button.Part = part;
            button.WindowPart = dwmPart;
            button.Bounds = new RectangleF(x, top, width, height);
            button.Enabled = enabled;
        }

        Add(FramePart.Close, row ? DwmPart.CloseButton : DwmPart.CloseAlone, VistaButtonMetrics.GlassCloseWidth, true);
        if (row)
        {
            Add(FramePart.Maximize, DwmPart.MaxButton, VistaButtonMetrics.GlassMaximizeWidth, maximize);
            Add(FramePart.Minimize, DwmPart.MinButton, VistaButtonMetrics.GlassMinimizeWidth, minimize);
        }

        geometry.ButtonCount = count;
    }

    private void PaintGlass(in FrameState state, in FrameInteraction interaction, double scale)
    {
        var list = List;
        var geometry = Geometry;
        var dwm = _style.DwmWindow!;
        var active = state.Active;
        var maximized = geometry.Maximized;
        var small = geometry.Small;
        var dwmState = maximized ? (active ? DwmPart.MaximizedActive : DwmPart.MaximizedInactive) : (active ? DwmPart.Active : DwmPart.Inactive);
        var frame = active ? 1 : 2;
        _glassScale = (float)scale;
        _glassList = list;
        var window = geometry.WindowRect;
        var client = geometry.ClientRect;
        var w = window.Width;
        var h = window.Height;
        var caption = geometry.CaptionHeight;
        var colorizationPart = small ? DwmPart.SmallCaption : DwmPart.CaptionColorization;

        var colorization = maximized
            ? _style.ColorizationAlpha(DwmPart.Caption, dwmState)
            : _style.ColorizationAlpha(colorizationPart, active ? 1 : 2);
        var color = ((uint)Math.Round(colorization * 255) << 24) | (_style.Colorization & 0x00ffffff);
        if (maximized)
        {
            Fill(geometry.CaptionRect, 0xff000000);
            Fill(geometry.CaptionRect, color);
        }
        else
        {
            FillRing(window, client, color);
        }

        var streak = _style.PartOpacity(DwmPart.Streak, dwmState);
        if (maximized)
        {
            Streak(dwm, geometry.CaptionRect, window, streak, ring: false, client, state.Placement);
        }
        else
        {
            Streak(dwm, window, window, streak, ring: true, client, state.Placement);
            Sheen(dwm, window, client, frame, _style.PartOpacity(DwmPart.SheenLeft, dwmState), _style.PartOpacity(DwmPart.SheenRight, dwmState));
            var band = _style.PartOpacity(DwmPart.Band, dwmState);
            Band(dwm, new RectangleF(window.Left + 1, window.Top + caption, 6, VistaButtonMetrics.BandHeight), frame, band);
            Band(dwm, new RectangleF(window.Right - 7, window.Top + caption, 6, VistaButtonMetrics.BandHeight), frame, band);
        }

        if (maximized)
        {
            var virtualCaption = RectangleF.FromLTRB(window.Left - geometry.Border, window.Top - geometry.Border, window.Right + geometry.Border, window.Top + caption);
            list.Clip(Device(geometry.CaptionRect, scale));
            CaptionBand(dwm, DwmPart.Caption, virtualCaption, frame);
            list.Restore();
        }
        else if (small)
        {
            SmallBand(dwm, DwmPart.SmallCaption, new RectangleF(window.Left, window.Top, w, caption), frame, vertical: false);
            SmallSide(dwm, DwmPart.SmallLeftBorder, RectangleF.FromLTRB(window.Left, window.Top + caption, window.Left + geometry.Border, window.Bottom - geometry.Border), frame);
            SmallSide(dwm, DwmPart.SmallRightBorder, RectangleF.FromLTRB(window.Right - geometry.Border, window.Top + caption, window.Right, window.Bottom - geometry.Border), frame);
            SmallBand(dwm, DwmPart.SmallBottomBorder, RectangleF.FromLTRB(window.Left, window.Bottom - geometry.Border, window.Right, window.Bottom), frame, vertical: true);
        }
        else
        {
            CaptionBand(dwm, DwmPart.Caption, new RectangleF(window.Left, window.Top, w, caption), frame);
            Side(dwm, DwmPart.LeftBorder, RectangleF.FromLTRB(window.Left, window.Top + caption, window.Left + geometry.Border, window.Bottom - geometry.Border), frame, outerFirst: true);
            Side(dwm, DwmPart.RightBorder, RectangleF.FromLTRB(window.Right - geometry.Border, window.Top + caption, window.Right, window.Bottom - geometry.Border), frame, outerFirst: false);
            BottomBand(dwm, DwmPart.BottomBorder, RectangleF.FromLTRB(window.Left, window.Bottom - geometry.Border, window.Right, window.Bottom), frame);
        }

        if (!maximized)
        {
            Shadow(dwm, DwmPart.ShadowLeft, RectangleF.FromLTRB(window.Left - geometry.Outsets.Left, window.Top, window.Left, window.Bottom), frame, vertical: true);
            Shadow(dwm, DwmPart.ShadowRight, RectangleF.FromLTRB(window.Right, window.Top, window.Right + geometry.Outsets.Right, window.Bottom), frame, vertical: true);
            Shadow(dwm, DwmPart.ShadowTop, RectangleF.FromLTRB(0, 0, geometry.Width, window.Top), frame, vertical: false);
            Shadow(dwm, DwmPart.ShadowBottom, RectangleF.FromLTRB(0, window.Bottom, geometry.Width, geometry.Height), frame, vertical: false);
        }

        _chromeOps = list.Count;
        Opaque = maximized;

        if (!geometry.IconRect.IsEmpty)
        {
            list.Icon(Device(geometry.IconRect, scale), state.Icon, state.AppId);
        }

        var captionPart = small ? (int)WindowPart.SmallCaption : maximized ? (int)WindowPart.MaxCaption : (int)WindowPart.Caption;
        if (!string.IsNullOrEmpty(state.Title) && geometry.TitleRect.Width > 0)
        {
            TitleGlow(dwm, state, colorizationPart, active ? 1 : 2, maximized, scale);
        }

        PaintTitle(state, captionPart, active ? 1 : 2, scale);
        var context = new MsStylesDrawContext(_style.Images, 96 * scale);
        foreach (ref readonly var button in geometry.Buttons)
        {
            if (button.Part != interaction.Hot && button.Part != interaction.PreviousHot)
            {
                GlassButtonLayers(dwm, button, active, maximized, interaction, context);
            }
        }

        foreach (ref readonly var button in geometry.Buttons)
        {
            if (button.Part == interaction.PreviousHot && button.Part != interaction.Hot)
            {
                GlassButtonLayers(dwm, button, active, maximized, interaction, context);
            }
        }

        foreach (ref readonly var button in geometry.Buttons)
        {
            if (button.Part == interaction.Hot)
            {
                GlassButtonLayers(dwm, button, active, maximized, interaction, context);
            }
        }
    }

    private void GlassButtonLayers(MsStylesClass dwm, in MsStylesFrameButton button, bool active, bool maximized, in FrameInteraction interaction, in MsStylesDrawContext context)
    {
        if (!button.Enabled)
        {
            GlassButton(dwm, button, active, maximized, ButtonDisabled, context, 1f);
            return;
        }

        if (interaction.Pressed == button.Part && interaction.Hot == button.Part)
        {
            GlassButton(dwm, button, active, maximized, ButtonPushed, context, 1f);
            return;
        }

        var (normal, hot) = HoverWeights(button.Part, interaction);
        GlassButton(dwm, button, active, maximized, ButtonNormal, context, normal);
        GlassButton(dwm, button, active, maximized, ButtonHot, context, hot);
    }

    private static (float Normal, float Hot) HoverWeights(FramePart part, in FrameInteraction interaction)
    {
        var hot = interaction.Hot == part && interaction.Pressed == FramePart.None;
        var elapsed = interaction.Now - interaction.HoverChangedAt;
        var fading = interaction.HoverChangedAt != 0 && interaction.Now != 0 && elapsed < VistaTransitions.HoverOutNanos
            && interaction.PressChangedAt < interaction.HoverChangedAt && interaction.Pressed == FramePart.None;
        if (fading && hot && interaction.PreviousHot != part)
        {
            return (VistaTransitions.FadeOut(elapsed), VistaTransitions.FadeIn(elapsed));
        }

        if (fading && !hot && interaction.PreviousHot == part && interaction.Hot != part)
        {
            return (VistaTransitions.FadeIn(elapsed), VistaTransitions.FadeOut(elapsed));
        }

        return hot ? (0f, 1f) : (1f, 0f);
    }

    internal static bool GlassAnimating(in FrameInteraction interaction)
    {
        if (interaction.HoverChangedAt == 0 || interaction.Now == 0 || interaction.Pressed != FramePart.None
            || interaction.PressChangedAt >= interaction.HoverChangedAt)
        {
            return false;
        }

        return interaction.Now - interaction.HoverChangedAt < VistaTransitions.HoverOutNanos
            && (IsButton(interaction.Hot) || IsButton(interaction.PreviousHot));
    }

    private static bool IsButton(FramePart part) => part is FramePart.Close or FramePart.Maximize or FramePart.Minimize;

    private void Fill(RectangleF logical, uint argb) => _glassList!.Fill(Device(logical, _glassScale), argb);

    private const int MaxRingSpans = 12;

    private void FillRing(RectangleF window, RectangleF client, uint argb)
    {
        Span<RectangleF> spans = stackalloc RectangleF[MaxRingSpans];
        var count = RingSpans(window, client, spans);
        for (var i = 0; i < count; i++)
        {
            Fill(spans[i], argb);
        }
    }

    private static int RingSpans(RectangleF window, RectangleF client, Span<RectangleF> spans)
    {
        var rows = CornerInsets.Length;
        var count = 0;
        for (var i = 0; i < rows; i++)
        {
            var inset = CornerInsets[i];
            spans[count++] = RectangleF.FromLTRB(window.Left + inset, window.Top + i, window.Right - inset, window.Top + i + 1);
            spans[count++] = RectangleF.FromLTRB(window.Left + inset, window.Bottom - i - 1, window.Right - inset, window.Bottom - i);
        }

        spans[count++] = RectangleF.FromLTRB(window.Left, window.Top + rows, window.Right, client.Top);
        spans[count++] = RectangleF.FromLTRB(window.Left, client.Top, client.Left, client.Bottom);
        spans[count++] = RectangleF.FromLTRB(client.Right, client.Top, window.Right, client.Bottom);
        spans[count++] = RectangleF.FromLTRB(window.Left, client.Bottom, window.Right, window.Bottom - rows);
        return count;
    }

    private bool Cell(MsStylesClass dwm, int part, int frame, out MsStylesBitmap bitmap, out Rectangle inner)
    {
        bitmap = null!;
        inner = default;
        if (!dwm.TryGetRect(part, 0, ThemeProperty.AtlasRect, out var rect) ||
            !dwm.TryGetImage(part, 0, ThemeProperty.AtlasImage, out var atlas) ||
            _style.Images.Get(atlas) is not { } image)
        {
            return false;
        }

        var count = dwm.TryGetInt(part, 0, ThemeProperty.ImageCount, out var imageCount) ? Math.Max(1, imageCount) : 1;
        var index = Math.Clamp(frame, 1, count) - 1;
        var height = rect.Height / count;
        bitmap = image;
        inner = Rectangle.FromLTRB(rect.Left + 1, rect.Top + index * height + 1, rect.Right - 1, rect.Top + (index + 1) * height - 1);
        return inner.Width > 0 && inner.Height > 0;
    }

    private void Piece(MsStylesBitmap bitmap, Rectangle cell, float sx0, float sy0, float sx1, float sy1, float dx0, float dy0, float dx1, float dy1, float opacity, bool smooth = false)
    {
        if (dx1 <= dx0 || dy1 <= dy0 || opacity <= 0)
        {
            return;
        }

        smooth |= (sx1 - sx0 > 1.01f && Math.Abs((sx1 - sx0) - (dx1 - dx0)) > 0.01f) ||
                  (sy1 - sy0 > 1.01f && Math.Abs((sy1 - sy0) - (dy1 - dy0)) > 0.01f);
        var source = RectangleF.FromLTRB(cell.X + sx0, cell.Y + sy0, cell.X + Math.Max(sx1, sx0 + 0.001f), cell.Y + Math.Max(sy1, sy0 + 0.001f));
        var destination = Device(RectangleF.FromLTRB(dx0, dy0, dx1, dy1), _glassScale);
        _glassList!.Image(bitmap.Handle, cell, source, destination, opacity, tileScale: _glassScale, smooth: smooth ? true : null);
    }

    private void CaptionBand(MsStylesClass dwm, int part, RectangleF dest, int frame)
    {
        if (!Cell(dwm, part, frame, out var bitmap, out var cell))
        {
            return;
        }

        var (outer, inner) = BorderSplit(dwm, DwmPart.LeftBorder);
        var top = Margins(dwm, part).Top;
        var bottom = Margins(dwm, part).Bottom;
        PackedColumns(bitmap, cell, dest, outer, inner, 0, top, dest.Top, dest.Top + top, 1f);
        PackedColumns(bitmap, cell, dest, outer, inner, top + 0.5f, cell.Height - bottom - 0.5f, dest.Top + top, dest.Bottom - bottom, 1f);
        PackedColumns(bitmap, cell, dest, outer, inner, cell.Height - bottom, cell.Height, dest.Bottom - bottom, dest.Bottom, 1f);
    }

    private void BottomBand(MsStylesClass dwm, int part, RectangleF dest, int frame)
    {
        if (!Cell(dwm, part, frame, out var bitmap, out var cell))
        {
            return;
        }

        var (outer, inner) = BorderSplit(dwm, DwmPart.LeftBorder);
        var margins = Margins(dwm, part);
        var topRows = margins.Top;
        PackedColumns(bitmap, cell, dest, outer, inner, 0, topRows, dest.Top, dest.Top + topRows, 1f);
        PackedColumns(bitmap, cell, dest, outer, inner, topRows + 1, cell.Height, dest.Top + topRows, dest.Bottom, 1f);
    }

    private void PackedColumns(MsStylesBitmap bitmap, Rectangle cell, RectangleF dest, int outer, int inner, float sy0, float sy1, float dy0, float dy1, float opacity)
    {
        var center = outer + 1 + inner;
        Piece(bitmap, cell, 0, sy0, outer, sy1, dest.Left, dy0, dest.Left + outer, dy1, opacity);
        Piece(bitmap, cell, outer + 1, sy0, center, sy1, dest.Left + outer, dy0, dest.Left + outer + inner, dy1, opacity);
        Piece(bitmap, cell, center, sy0, center + 1, sy1, dest.Left + outer + inner, dy0, dest.Right - outer - inner, dy1, opacity);
        Piece(bitmap, cell, center + 1, sy0, center + 1 + inner, sy1, dest.Right - outer - inner, dy0, dest.Right - outer, dy1, opacity);
        Piece(bitmap, cell, cell.Width - outer, sy0, cell.Width, sy1, dest.Right - outer, dy0, dest.Right, dy1, opacity);
    }

    private void Side(MsStylesClass dwm, int part, RectangleF dest, int frame, bool outerFirst)
    {
        if (!Cell(dwm, part, frame, out var bitmap, out var cell))
        {
            return;
        }

        var margins = Margins(dwm, part);
        var first = margins.Left;
        var second = margins.Right;
        Piece(bitmap, cell, 0, 0, first, 1, dest.Left, dest.Top, dest.Left + first, dest.Bottom, 1f);
        Piece(bitmap, cell, first + 1, 0, first + 1 + second, 1, dest.Left + first, dest.Top, dest.Left + first + second, dest.Bottom, 1f);
    }

    private void SmallBand(MsStylesClass dwm, int part, RectangleF dest, int frame, bool vertical)
    {
        if (!Cell(dwm, part, frame, out var bitmap, out var cell))
        {
            return;
        }

        var (side, _) = SmallSplit(dwm);
        var margins = Margins(dwm, part);
        var top = margins.Top;
        var bottom = margins.Bottom;
        void Columns(float sy0, float sy1, float dy0, float dy1)
        {
            var middle = cell.Width / 2f;
            Piece(bitmap, cell, 0, sy0, side, sy1, dest.Left, dy0, dest.Left + side, dy1, 1f);
            Piece(bitmap, cell, side + 0.5f, sy0, side + 0.5f, sy1, dest.Left + side, dy0, dest.Left + side + 2, dy1, 1f);
            Piece(bitmap, cell, side + 1, sy0, 2 * side + 1, sy1, dest.Left + side + 2, dy0, dest.Left + 2 * side + 2, dy1, 1f);
            Piece(bitmap, cell, middle - 0.5f, sy0, middle + 0.5f, sy1, dest.Left + 2 * side + 2, dy0, dest.Right - 2 * side - 2, dy1, 1f);
            Piece(bitmap, cell, cell.Width - 2 * side - 1, sy0, cell.Width - side - 1, sy1, dest.Right - 2 * side - 2, dy0, dest.Right - side - 2, dy1, 1f);
            Piece(bitmap, cell, cell.Width - side - 0.5f, sy0, cell.Width - side - 0.5f, sy1, dest.Right - side - 2, dy0, dest.Right - side, dy1, 1f);
            Piece(bitmap, cell, cell.Width - side, sy0, cell.Width, sy1, dest.Right - side, dy0, dest.Right, dy1, 1f);
        }

        Columns(0, top, dest.Top, dest.Top + top);
        if (vertical)
        {
            Columns(top + 0.5f, top + 0.5f, dest.Top + top, dest.Bottom - bottom);
        }
        else
        {
            Columns(top + 0.5f, cell.Height - bottom - 0.5f, dest.Top + top, dest.Bottom - bottom);
        }

        Columns(cell.Height - bottom, cell.Height, dest.Bottom - bottom, dest.Bottom);
    }

    private void SmallSide(MsStylesClass dwm, int part, RectangleF dest, int frame)
    {
        if (!Cell(dwm, part, frame, out var bitmap, out var cell))
        {
            return;
        }

        var (side, _) = SmallSplit(dwm);
        Piece(bitmap, cell, 0, 0, side, 1, dest.Left, dest.Top, dest.Left + side, dest.Bottom, 1f);
        Piece(bitmap, cell, side + 0.5f, 0, side + 0.5f, 1, dest.Left + side, dest.Top, dest.Right - side, dest.Bottom, 1f);
        Piece(bitmap, cell, cell.Width - side, 0, cell.Width, 1, dest.Right - side, dest.Top, dest.Right, dest.Bottom, 1f);
    }

    private void Shadow(MsStylesClass dwm, int part, RectangleF dest, int frame, bool vertical)
    {
        if (!Cell(dwm, part, frame, out var bitmap, out var cell))
        {
            return;
        }

        var margins = Margins(dwm, part);
        if (vertical)
        {
            var first = margins.Top;
            var length = cell.Height;
            Piece(bitmap, cell, 0.5f, 0, cell.Width - 0.5f, first, dest.Left, dest.Top, dest.Right, dest.Top + first, 1f, smooth: true);
            Piece(bitmap, cell, 0.5f, first + 0.5f, cell.Width - 0.5f, first + 0.5f, dest.Left, dest.Top + first, dest.Right, dest.Bottom - (length - first - 1), 1f, smooth: true);
            Piece(bitmap, cell, 0.5f, first + 1, cell.Width - 0.5f, length, dest.Left, dest.Bottom - (length - first - 1), dest.Right, dest.Bottom, 1f, smooth: true);
        }
        else
        {
            var first = margins.Left;
            var length = cell.Width;
            Piece(bitmap, cell, 0, 0.5f, first, cell.Height - 0.5f, dest.Left, dest.Top, dest.Left + first, dest.Bottom, 1f, smooth: true);
            Piece(bitmap, cell, first + 0.5f, 0.5f, first + 0.5f, cell.Height - 0.5f, dest.Left + first, dest.Top, dest.Right - (length - first - 1), dest.Bottom, 1f, smooth: true);
            Piece(bitmap, cell, first + 1, 0.5f, length, cell.Height - 0.5f, dest.Right - (length - first - 1), dest.Top, dest.Right, dest.Bottom, 1f, smooth: true);
        }
    }

    private void Streak(MsStylesClass dwm, RectangleF area, RectangleF window, float opacity, bool ring, RectangleF client, in FramePlacement placement)
    {
        if (opacity <= 0 || !Cell(dwm, DwmPart.Streak, 1, out var bitmap, out var cell))
        {
            return;
        }

        float scaleX = 1, scaleY = 1, offsetX, offsetY;
        if (placement.IsEmpty)
        {
            var anchorX = ring ? Math.Clamp(VistaButtonMetrics.StreakAnchorX, 0, Math.Max(0, cell.Width - (int)window.Width)) : VistaButtonMetrics.StreakMaximizedAnchor;
            var anchorY = ring ? Math.Clamp(VistaButtonMetrics.StreakAnchorY, 0, Math.Max(0, cell.Height - (int)window.Height)) : VistaButtonMetrics.StreakMaximizedAnchor;
            offsetX = anchorX - window.Left;
            offsetY = anchorY - window.Top;
        }
        else
        {
            var toOutputX = placement.X - client.Left;
            var toOutputY = placement.Y - client.Top;
            var halfWidth = placement.OutputWidth / 2f;
            var centerX = (int)MathF.Round(window.Left + toOutputX) + ((int)MathF.Round(window.Width) / 2);
            var drag = (centerX - (placement.OutputWidth / 2)) / VistaButtonMetrics.StreakDragDivisor;
            scaleX = (float)cell.Width / placement.OutputWidth;
            scaleY = (float)cell.Height / placement.OutputHeight;
            offsetX = (cell.Width / 2f) + ((toOutputX + 0.5f - halfWidth) * scaleX) - (drag * scaleX) + 0.5f;
            offsetY = ((toOutputY + 0.5f) * scaleY) + 0.5f;
        }

        void Span(float left, float top, float right, float bottom)
        {
            Piece(
                bitmap, cell,
                (scaleX * left) + offsetX, (scaleY * top) + offsetY,
                (scaleX * right) + offsetX, (scaleY * bottom) + offsetY,
                left, top, right, bottom, opacity);
        }

        if (!ring)
        {
            Span(area.Left, area.Top, area.Right, area.Bottom);
            return;
        }

        var inner = RectangleF.Inflate(window, -1, -1);
        Span<RectangleF> spans = stackalloc RectangleF[MaxRingSpans];
        var count = RingSpans(inner, client, spans);
        for (var i = 0; i < count; i++)
        {
            Span(spans[i].Left, spans[i].Top, spans[i].Right, spans[i].Bottom);
        }
    }

    private void Sheen(MsStylesClass dwm, RectangleF window, RectangleF client, int frame, float left, float right)
    {
        var inner = RectangleF.Inflate(window, -1, -1);
        var center = window.Left + MathF.Floor(window.Width / 2);
        var height = MathF.Round(0.75f * (window.Height - 4)) - 1;
        var bottom = inner.Top + height;
        Span<RectangleF> spans = stackalloc RectangleF[MaxRingSpans];
        var count = RingSpans(inner, client, spans);
        if (left > 0 && Cell(dwm, DwmPart.SheenLeft, frame, out var leftBitmap, out var leftCell))
        {
            for (var i = 0; i < count; i++)
            {
                var span = spans[i];
                var r = MathF.Min(span.Right, center);
                var b = MathF.Min(span.Bottom, bottom);
                if (r > span.Left && b > span.Top)
                {
                    Piece(leftBitmap, leftCell, span.Left - inner.Left, span.Top - inner.Top, r - inner.Left, b - inner.Top, span.Left, span.Top, r, b, left);
                }
            }
        }

        if (right > 0 && Cell(dwm, DwmPart.SheenRight, frame, out var rightBitmap, out var rightCell))
        {
            var offset = rightCell.Width - inner.Right;
            for (var i = 0; i < count; i++)
            {
                var span = spans[i];
                var l = MathF.Max(span.Left, center);
                var b = MathF.Min(span.Bottom, bottom);
                if (span.Right > l && b > span.Top)
                {
                    Piece(rightBitmap, rightCell, l + offset, span.Top - inner.Top, span.Right + offset, b - inner.Top, l, span.Top, span.Right, b, right);
                }
            }
        }
    }

    private void Band(MsStylesClass dwm, RectangleF dest, int frame, float opacity)
    {
        if (opacity <= 0 || !Cell(dwm, DwmPart.Band, frame, out var bitmap, out var cell))
        {
            return;
        }

        var bottom = Margins(dwm, DwmPart.Band).Bottom;
        Piece(bitmap, cell, 0.5f, 0.5f, cell.Width - 0.5f, cell.Height - bottom - 1.5f, dest.Left, dest.Top, dest.Right, dest.Bottom - bottom, opacity, smooth: true);
        Piece(bitmap, cell, 0.5f, cell.Height - bottom, cell.Width - 0.5f, cell.Height, dest.Left, dest.Bottom - bottom, dest.Right, dest.Bottom, opacity, smooth: true);
    }

    private void TitleGlow(MsStylesClass dwm, in FrameState state, int part, int state12, bool maximized, double scale)
    {
        var opacity = maximized ? 0 : _style.PartOpacity(part, state12);
        if (opacity <= 0 || !Cell(dwm, DwmPart.TitleGlow, 1, out var bitmap, out var cell))
        {
            return;
        }

        var title = Geometry.TitleRect;
        var window = Geometry.WindowRect;
        var content = dwm.TryGetMargins(DwmPart.TitleGlow, 0, ThemeProperty.ContentMargins, out var contentMargins) ? contentMargins : default;
        var margins = Margins(dwm, DwmPart.TitleGlow);
        var textWidth = MathF.Min(title.Width, EstimateTitleWidth(state, scale));
        var left = title.Left - content.Left;
        var right = MathF.Max(left + margins.Left + margins.Right, title.Left + textWidth + content.Right);
        var dest = RectangleF.FromLTRB(left, window.Top, right, window.Top + Geometry.CaptionHeight - 1);
        var top = (float)margins.Top;
        var bottom = (float)margins.Bottom;
        if (top + bottom > dest.Height)
        {
            top = MathF.Round(top * dest.Height / (margins.Top + margins.Bottom));
            bottom = dest.Height - top;
        }

        void Columns(float sy0, float sy1, float dy0, float dy1)
        {
            var center = margins.Left + 0.5f;
            Piece(bitmap, cell, 0, sy0, margins.Left, sy1, dest.Left, dy0, dest.Left + margins.Left, dy1, opacity, smooth: true);
            Piece(bitmap, cell, center, sy0, center + 1, sy1, dest.Left + margins.Left, dy0, dest.Right - margins.Right, dy1, opacity, smooth: true);
            Piece(bitmap, cell, cell.Width - margins.Right, sy0, cell.Width, sy1, dest.Right - margins.Right, dy0, dest.Right, dy1, opacity, smooth: true);
        }

        Columns(margins.Top - top, margins.Top, dest.Top, dest.Top + top);
        var middle = dest.Height - top - bottom;
        if (middle > 0)
        {
            Columns(margins.Top + 0.5f, cell.Height - margins.Bottom - 0.5f, dest.Top + top, dest.Top + top + middle);
        }

        Columns(cell.Height - margins.Bottom, cell.Height - margins.Bottom + bottom, dest.Bottom - bottom, dest.Bottom);
    }

    private void GlassButton(MsStylesClass dwm, in MsStylesFrameButton button, bool active, bool maximized, int buttonState, in MsStylesDrawContext context, float opacity)
    {
        if (opacity <= 0)
        {
            return;
        }

        var part = button.WindowPart + (active || button.WindowPart == DwmPart.SmallCloseButton ? 0 : 1);
        if (!Cell(dwm, part, buttonState, out var bitmap, out var cell))
        {
            return;
        }

        var dest = button.Bounds;
        var margins = Margins(dwm, part);
        var center = (float)cell.Width - margins.Left - margins.Right;
        void Columns(float sy0, float sy1, float dy0, float dy1)
        {
            Piece(bitmap, cell, 0, sy0, margins.Left, sy1, dest.Left, dy0, dest.Left + margins.Left, dy1, opacity);
            Piece(bitmap, cell, margins.Left + 0.5f, sy0, margins.Left + center - 0.5f, sy1, dest.Left + margins.Left, dy0, dest.Right - margins.Right, dy1, opacity);
            Piece(bitmap, cell, cell.Width - margins.Right, sy0, cell.Width, sy1, dest.Right - margins.Right, dy0, dest.Right, dy1, opacity);
        }

        var visibleTop = dest.Height < cell.Height ? cell.Height - dest.Height : 0;
        Columns(visibleTop, margins.Top, dest.Top, dest.Top + margins.Top - visibleTop);
        Columns(margins.Top + 0.5f, cell.Height - margins.Bottom - 0.5f, dest.Top + margins.Top - visibleTop, dest.Bottom - margins.Bottom);
        Columns(cell.Height - margins.Bottom, cell.Height, dest.Bottom - margins.Bottom, dest.Bottom);
        if (buttonState is ButtonHot or ButtonPushed)
        {
            ButtonGlow(dwm, button, opacity);
        }

        var glyphBase = button.Part switch
        {
            FramePart.Close => button.WindowPart == DwmPart.SmallCloseButton ? DwmPart.SmallCloseGlyph : DwmPart.CloseGlyph,
            FramePart.Maximize => maximized ? DwmPart.RestoreGlyph : DwmPart.MaxGlyph,
            _ => DwmPart.MinGlyph,
        };
        var glyph = glyphBase + GlyphSize(context.Dpi);
        while (glyph > glyphBase && !Cell(dwm, glyph, buttonState, out _, out _))
        {
            glyph--;
        }

        if (!Cell(dwm, glyph, buttonState, out var glyphBitmap, out var glyphCell))
        {
            return;
        }

        var content = dwm.TryGetMargins(part, 0, ThemeProperty.ContentMargins, out var contentMargins) ? contentMargins : default;
        var box = RectangleF.FromLTRB(dest.Left + content.Left, dest.Top + content.Top, dest.Right - content.Right, dest.Bottom - content.Bottom);
        var device = Device(box, _glassScale);
        var gx = device.Left + MathF.Floor((device.Width - glyphCell.Width) / 2);
        var gy = device.Top + MathF.Floor((device.Height - glyphCell.Height) / 2);
        _glassList!.Image(glyphBitmap.Handle, glyphCell, glyphCell, new RectangleF(gx, gy, glyphCell.Width, glyphCell.Height), opacity, tileScale: 1f);
    }

    private static int GlowPart(in MsStylesFrameButton button) => button.WindowPart switch
    {
        DwmPart.SmallCloseButton => DwmPart.SmallCloseGlow,
        DwmPart.CloseButton or DwmPart.CloseAlone => DwmPart.CloseGlow,
        _ => DwmPart.ButtonGlow,
    };

    internal RectangleF GlowBounds(MsStylesClass dwm, in MsStylesFrameButton button)
    {
        var margins = Margins(dwm, GlowPart(button));
        var dest = button.Bounds;
        return RectangleF.FromLTRB(dest.Left - margins.Left, dest.Top - margins.Top, dest.Right + margins.Right, dest.Bottom + margins.Bottom);
    }

    private void ButtonGlow(MsStylesClass dwm, in MsStylesFrameButton button, float opacity)
    {
        var part = GlowPart(button);
        if (!Cell(dwm, part, 1, out var bitmap, out var cell))
        {
            return;
        }

        var margins = Margins(dwm, part);
        var dest = GlowBounds(dwm, button);
        void Row(float sy0, float sy1, float dy0, float dy1)
        {
            Piece(bitmap, cell, 0, sy0, margins.Left, sy1, dest.Left, dy0, dest.Left + margins.Left, dy1, opacity);
            Piece(bitmap, cell, margins.Left + 0.5f, sy0, cell.Width - margins.Right - 0.5f, sy1, dest.Left + margins.Left, dy0, dest.Right - margins.Right, dy1, opacity);
            Piece(bitmap, cell, cell.Width - margins.Right, sy0, cell.Width, sy1, dest.Right - margins.Right, dy0, dest.Right, dy1, opacity);
        }

        Row(0, margins.Top, dest.Top, dest.Top + margins.Top);
        Row(margins.Top + 0.5f, cell.Height - margins.Bottom - 0.5f, dest.Top + margins.Top, dest.Bottom - margins.Bottom);
        Row(cell.Height - margins.Bottom, cell.Height, dest.Bottom - margins.Bottom, dest.Bottom);
    }

    private static int GlyphSize(double dpi) => dpi >= 192 ? 3 : dpi >= 144 ? 2 : dpi >= 120 ? 1 : 0;

    private float EstimateTitleWidth(in FrameState state, double scale)
    {
        var font = Geometry.Small ? _style.SmallCaptionFont : _style.CaptionFont;
        var pixels = (float)(font.Points * 96.0 / 72.0);
        return TextMetrics is { } metrics && state.Title is { } title
            ? metrics.Measure(title, font, pixels)
            : (state.Title?.Length ?? 0) * pixels * 0.5f;
    }

    /// <summary>
    /// Gets or sets the text measurer that sizes the glass glow plate around the title. When it is <see langword="null"/>, the width is estimated from the character count.
    /// </summary>
    public IMsStylesTextMetrics? TextMetrics { get; set; }

    private static MsStylesMargins Margins(MsStylesClass dwm, int part) =>
        dwm.TryGetMargins(part, 0, ThemeProperty.SizingMargins, out var margins) ? margins : default;

    private static (int Outer, int Inner) BorderSplit(MsStylesClass dwm, int part)
    {
        var margins = Margins(dwm, part);
        return (Math.Max(0, margins.Left), Math.Max(0, margins.Right));
    }

    private static (int Side, int Unused) SmallSplit(MsStylesClass dwm)
    {
        var margins = Margins(dwm, DwmPart.SmallLeftBorder);
        return (Math.Max(0, margins.Left), 0);
    }

    /// <summary>
    /// Fills the region of the glass ring, the window minus the client with rounded corners, where the backdrop effect shows.
    /// </summary>
    /// <param name="into">The region to fill in logical surface coordinates. The method clears it first.</param>
    /// <returns><see langword="true"/> for a laid-out glass frame that is not maximized.</returns>
    public bool GlassBackdropRegion(Pixman.PixmanRegion32 into)
    {
        into.Clear();
        if (!_style.IsGlass || !LaidOut)
        {
            return false;
        }

        var geometry = Geometry;
        if (geometry.Maximized)
        {
            return false;
        }

        Span<RectangleF> spans = stackalloc RectangleF[MaxRingSpans];
        var count = RingSpans(geometry.WindowRect, geometry.ClientRect, spans);
        for (var i = 0; i < count; i++)
        {
            var span = spans[i];
            if (span.Width > 0 && span.Height > 0)
            {
                into.UnionRect(into, (int)span.Left, (int)span.Top, (uint)span.Width, (uint)span.Height);
            }
        }

        return true;
    }
}
