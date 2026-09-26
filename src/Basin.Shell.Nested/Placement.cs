namespace Basin.Shell.Nested;

public static class Placement
{
    public const int CascadeStep = 25;

    public const int CascadeFuzz = 15;

    public const int CascadeInterval = 50;

    public static Point Place(in PlacementRequest request)
    {
        if (request.ParentFrame is { } parent)
            return Clamp(
                request.Mode == PlacementMode.Maximize ? Centered(parent, request.Width, request.Height) : OverParent(request, parent),
                request.WorkArea, request.Width, request.Height);
        if (request.Mode == PlacementMode.Maximize)
            return new Point(request.WorkArea.X, request.WorkArea.Y);
        if (request.Mode != PlacementMode.Automatic)
            return Clamp(UnderPointer(request), request.Output, request.Width, request.Height);
        if (FirstFit(request) is { } fit)
            return fit;
        if (request.CenterNewWindows)
            return Clamp(Centered(request.WorkArea, request.Width, request.Height), request.WorkArea, request.Width, request.Height);
        return NextCascade(request);
    }

    public static bool ShouldMaximize(int width, int height, Box workArea) =>
        width >= workArea.Width && height >= workArea.Height;

    private static Point OverParent(in PlacementRequest request, Box parent)
    {
        var x = parent.X + (parent.Width - request.Width) / 2;
        var y = parent.Y + request.ParentTitleHeight + (parent.Height - request.ParentTitleHeight - request.Height) / 3;
        return new Point(x, y);
    }

    private static Point UnderPointer(in PlacementRequest request) =>
        new(request.Pointer.X - request.Width / 2, request.Pointer.Y - request.Height / 2);

    private static Point Centered(Box area, int width, int height) =>
        new(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2);

    private static Point Clamp(Point point, Box area, int width, int height) =>
        new(Math.Max(area.X, Math.Min(point.X, area.Right - width)),
            Math.Max(area.Y, Math.Min(point.Y, area.Bottom - height)));

    private static Point? FirstFit(in PlacementRequest request)
    {
        var workArea = request.WorkArea;
        var origin = new Point(workArea.X, workArea.Y);
        if (Fits(origin, request))
            return origin;
        var candidates = request.Visible
            .SelectMany(w => new[] { new Point(w.Right, w.Y), new Point(w.X, w.Bottom) })
            .OrderBy(p => DistanceFromOrigin(p, workArea))
            .ThenBy(p => p.Y)
            .ThenBy(p => p.X);
        foreach (var candidate in candidates)
        {
            if (Fits(candidate, request))
                return candidate;
        }
        return null;
    }

    private static bool Fits(Point at, in PlacementRequest request)
    {
        var box = new Box(at.X, at.Y, request.Width, request.Height);
        return request.WorkArea.Contains(box) && !request.Visible.Any(w => Overlaps(box, w));
    }

    private static bool Overlaps(Box a, Box b) => !a.Intersect(b).IsEmpty;

    private static long DistanceFromOrigin(Point p, Box workArea)
    {
        long dx = p.X - workArea.X;
        long dy = p.Y - workArea.Y;
        return dx * dx + dy * dy;
    }

    private static Point NextCascade(in PlacementRequest request)
    {
        var workArea = request.WorkArea;
        var sorted = request.Visible
            .OrderBy(w => (long)w.X * w.X + (long)w.Y * w.Y)
            .ToList();
        var x = Math.Max(0, workArea.X);
        var y = Math.Max(0, workArea.Y);
        var stage = 0;
        var i = 0;
        while (i < sorted.Count)
        {
            var w = sorted[i];
            if (Math.Abs(w.X - x) < CascadeFuzz && Math.Abs(w.Y - y) < CascadeFuzz)
            {
                x = w.X + CascadeStep;
                y = w.Y + CascadeStep;
                if (x + request.Width > workArea.Right || y + request.Height > workArea.Bottom)
                {
                    stage++;
                    x = Math.Max(0, workArea.X) + CascadeInterval * stage;
                    y = Math.Max(0, workArea.Y);
                    if (x + request.Width < workArea.Right)
                    {
                        i = 0;
                        continue;
                    }
                    x = Math.Max(0, workArea.X);
                    break;
                }
            }
            i++;
        }
        return new Point(x, y);
    }
}
