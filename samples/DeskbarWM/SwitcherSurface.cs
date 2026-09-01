using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace DeskbarWm;

internal sealed class SwitcherSurface : IDisposable
{
    private const int IconSlot = 56;
    private const int IconSize = 48;
    private const int Padding = 12;
    private const int TextBlock = 40;

    private readonly ManagerSurface _surface;
    private string? _lastKey;

    internal SwitcherSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm)
    {
        Output = output;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Overlay, "deskbar-switcher");
        _surface.SetExclusiveZone(-1);
        _surface.Configured += wm.RequestManage;
    }

    public WmOutput Output { get; }

    public uint SurfaceId => _surface.SurfaceId;

    public bool Render(SwitcherState state, IconCache icons, int scale)
    {
        var teams = state.Teams;
        var width = Math.Max((teams.Count * IconSlot) + (Padding * 2), 240);
        var height = IconSlot + TextBlock + (Padding * 2);
        var desired = new Size(width, height);
        if (_surface.ConfiguredSize != desired)
        {
            _surface.SetSize(desired.Width, desired.Height);
            if (!_surface.IsConfigured)
            {
                _surface.CommitInitial();
            }
            else
            {
                _surface.Surface.Commit();
            }

            return false;
        }

        var key = RenderKey(state, scale);
        if (key == _lastKey)
        {
            return false;
        }

        var pixels = _surface.Prepare(desired.Width, desired.Height, scale);
        if (pixels == 0)
        {
            return false;
        }

        using var canvas = _surface.CreateCanvas(pixels);
        if (canvas is null)
        {
            return false;
        }

        canvas.Canvas.Scale(scale);
        Draw(canvas.Canvas, desired, state, icons);
        canvas.Canvas.Flush();
        _surface.SetInputRegion(Rect.Empty);
        _lastKey = key;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Dispose() => _surface.Dispose();

    private static string RenderKey(SwitcherState state, int scale)
    {
        var key = $"{scale}|{state.TeamIndex}|{state.WindowIndex}";
        foreach (var team in state.Teams)
        {
            key += $"|{team.DisplayName}:{team.Windows.Count}";
        }

        return key;
    }

    private static void Draw(SKCanvas canvas, Size size, SwitcherState state, IconCache icons)
    {
        var panel = new SKColor(216, 216, 216);
        using var paint = new SKPaint();
        paint.IsAntialias = false;

        canvas.Clear(SKColors.Transparent);
        paint.Color = panel;
        canvas.DrawRect(0, 0, size.Width, size.Height, paint);
        paint.Color = Theme.Tint(panel, Theme.Lighten2);
        canvas.DrawRect(0, 0, size.Width, 1, paint);
        canvas.DrawRect(0, 0, 1, size.Height, paint);
        paint.Color = Theme.Tint(panel, Theme.Darken2);
        canvas.DrawRect(0, size.Height - 1, size.Width, 1, paint);
        canvas.DrawRect(size.Width - 1, 0, 1, size.Height, paint);

        using var font = new SKFont(Fonts.Sans, Theme.FontSize);
        var teams = state.Teams;
        var rowWidth = teams.Count * IconSlot;
        var startX = (size.Width - rowWidth) / 2;
        for (var i = 0; i < teams.Count; i++)
        {
            var slot = new Rect(startX + (i * IconSlot), Padding, IconSlot, IconSlot);
            if (i == state.TeamIndex)
            {
                paint.Color = Theme.Tint(panel, Theme.DarkenHalf);
                canvas.DrawRect(slot.X, slot.Y, slot.Width, slot.Height, paint);
                paint.Color = new SKColor(51, 102, 152);
                canvas.DrawRect(slot.X, slot.Y, slot.Width, 2, paint);
                canvas.DrawRect(slot.X, slot.Bottom - 2, slot.Width, 2, paint);
                canvas.DrawRect(slot.X, slot.Y, 2, slot.Height, paint);
                canvas.DrawRect(slot.Right - 2, slot.Y, 2, slot.Height, paint);
            }

            var icon = teams[i].AppId is { Length: > 0 } appId ? icons.Load(appId, IconSize) : null;
            var iconX = slot.X + ((IconSlot - IconSize) / 2);
            var iconY = slot.Y + ((IconSlot - IconSize) / 2);
            if (icon is { } image)
            {
                canvas.DrawImage(
                    image,
                    new SKRect(iconX, iconY, iconX + IconSize, iconY + IconSize),
                    new SKSamplingOptions(SKFilterMode.Linear));
            }
            else
            {
                paint.Color = Theme.Tint(panel, Theme.Darken1);
                canvas.DrawRect(iconX, iconY, IconSize, IconSize, paint);
            }
        }

        paint.IsAntialias = true;
        var metrics = font.Metrics;
        var nameBaseline = Padding + IconSlot + 16;
        if (state.SelectedTeam is { } selected)
        {
            paint.Color = SKColors.Black;
            var name = selected.DisplayName;
            canvas.DrawText(
                name,
                (size.Width - font.MeasureText(name)) / 2f,
                nameBaseline,
                SKTextAlign.Left,
                font,
                paint);

            if (state.SelectedWindow is { } window)
            {
                var title = Fonts.Ellipsize(font, window.Window.Title ?? string.Empty, size.Width - 20);
                paint.Color = new SKColor(96, 96, 96);
                canvas.DrawText(
                    title,
                    (size.Width - font.MeasureText(title)) / 2f,
                    nameBaseline + metrics.Descent - metrics.Ascent + 2,
                    SKTextAlign.Left,
                    font,
                    paint);
            }
        }

        paint.IsAntialias = false;
    }
}
