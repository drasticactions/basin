using System.Numerics;

namespace Basin.SoftKeyboard;

public sealed class TraceDecoder
{
    public const int Samples = 64;
    public const int MaxResults = 8;

    private readonly WordList _words;
    private readonly Dictionary<(char First, char Last), List<int>> _index = [];
    private readonly Dictionary<char, Vector2> _centers = [];
    private readonly Vector2[] _path = new Vector2[Samples];
    private readonly Vector2[] _ideal = new Vector2[Samples];
    private readonly Vector2[] _normalPath = new Vector2[Samples];
    private readonly Vector2[] _normalIdeal = new Vector2[Samples];
    private readonly List<Vector2> _keys = new(32);
    private readonly HashSet<char> _starts = [];
    private readonly HashSet<char> _ends = [];

    public TraceDecoder(WordList words)
    {
        ArgumentNullException.ThrowIfNull(words);
        _words = words;
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (word.Length == 0)
            {
                continue;
            }

            var key = (char.ToLowerInvariant(word[0]), char.ToLowerInvariant(word[^1]));
            if (!_index.TryGetValue(key, out var bucket))
            {
                bucket = [];
                _index[key] = bucket;
            }

            bucket.Add(i);
        }
    }

    public WordList Words => _words;

    public double ShapeSigma { get; set; } = 0.1;

    public double LocationSigma { get; set; } = 0.15;

    public double PriorWeight { get; set; } = 3;

    public static bool TracesLetter(KeyCap? cap) =>
        cap is { IsText: true } && cap.Text.Length == 1 && char.IsLetter(cap.Text[0]);

    public IReadOnlyList<TraceCandidate> Decode(ReadOnlySpan<Vector2> path, KeyGrid grid, ReadOnlySpan<KeyRect> rects)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (path.Length < 2)
        {
            return [];
        }

        _centers.Clear();
        var unit = float.MaxValue;
        for (var cell = 0; cell < grid.CellCount; cell++)
        {
            var key = grid[cell];
            if (!key.IsVisible || !TracesLetter(key.Plain))
            {
                continue;
            }

            var c = char.ToLowerInvariant(key.Plain!.Text[0]);
            if (!_centers.ContainsKey(c))
            {
                _centers[c] = new Vector2(rects[cell].CenterX, rects[cell].CenterY);
                unit = Math.Min(unit, rects[cell].Width);
            }
        }

        if (_centers.Count == 0)
        {
            return [];
        }

        Resample(path, _path);
        var pathLength = Length(path);
        Normalize(_path, _normalPath);
        Nearby(_path[0], unit, _starts);
        Nearby(_path[^1], unit, _ends);
        var results = new List<TraceCandidate>(MaxResults + 1);
        foreach (var first in _starts)
        {
            foreach (var last in _ends)
            {
                if (!_index.TryGetValue((first, last), out var bucket))
                {
                    continue;
                }

                foreach (var index in bucket)
                {
                    Consider(index, pathLength, unit, results);
                }
            }
        }

        results.Sort(static (a, b) => b.Score.CompareTo(a.Score) is var order && order != 0 ? order : a.Word.Length.CompareTo(b.Word.Length));
        if (results.Count > MaxResults)
        {
            results.RemoveRange(MaxResults, results.Count - MaxResults);
        }

        return results;
    }

    private void Consider(int index, float pathLength, float unit, List<TraceCandidate> results)
    {
        var word = _words[index];
        if (word.Length < 2)
        {
            return;
        }

        _keys.Clear();
        foreach (var c in word)
        {
            if (!_centers.TryGetValue(char.ToLowerInvariant(c), out var center))
            {
                return;
            }

            if (_keys.Count == 0 || _keys[^1] != center)
            {
                _keys.Add(center);
            }
        }

        var idealLength = 0f;
        for (var i = 1; i < _keys.Count; i++)
        {
            idealLength += Vector2.Distance(_keys[i - 1], _keys[i]);
        }

        if (idealLength < unit * 0.5f)
        {
            if (pathLength > unit * 1.5f)
            {
                return;
            }
        }
        else if (pathLength < idealLength * 0.5f || pathLength > (idealLength * 2.2f) + unit)
        {
            return;
        }

        Resample(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_keys), _ideal);
        Normalize(_ideal, _normalIdeal);
        var shape = 0.0;
        var location = 0.0;
        var weights = 0.0;
        for (var i = 0; i < Samples; i++)
        {
            shape += Vector2.Distance(_normalPath[i], _normalIdeal[i]);
            var edge = Math.Abs((i / (double)(Samples - 1)) - 0.5) * 2;
            var weight = 0.5 + edge;
            location += weight * Vector2.Distance(_path[i], _ideal[i]);
            weights += weight;
        }

        shape /= Samples;
        location = location / weights / unit;
        var score = -(shape * shape / (2 * ShapeSigma * ShapeSigma))
            - (location * location / (2 * LocationSigma * LocationSigma))
            + (PriorWeight * _words.Prior(index));
        if (results.Count == MaxResults + 1 && score <= results[^1].Score)
        {
            return;
        }

        var existing = results.FindIndex(candidate => string.Equals(candidate.Word, word, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            if (results[existing].Score >= score)
            {
                return;
            }

            results.RemoveAt(existing);
        }

        var at = results.FindIndex(candidate => candidate.Score < score);
        results.Insert(at < 0 ? results.Count : at, new TraceCandidate(word, score));
        if (results.Count > MaxResults + 1)
        {
            results.RemoveAt(results.Count - 1);
        }
    }

    private void Nearby(Vector2 point, float unit, HashSet<char> into)
    {
        into.Clear();
        var nearest = '\0';
        var best = float.MaxValue;
        foreach (var (c, center) in _centers)
        {
            var distance = Vector2.Distance(point, center);
            if (distance < unit * 1.15f)
            {
                into.Add(c);
            }

            if (distance < best)
            {
                best = distance;
                nearest = c;
            }
        }

        into.Add(nearest);
    }

    private static float Length(ReadOnlySpan<Vector2> points)
    {
        var length = 0f;
        for (var i = 1; i < points.Length; i++)
        {
            length += Vector2.Distance(points[i - 1], points[i]);
        }

        return length;
    }

    private static void Resample(ReadOnlySpan<Vector2> points, Span<Vector2> into)
    {
        if (points.Length == 1)
        {
            into.Fill(points[0]);
            return;
        }

        var total = Length(points);
        if (total <= 0)
        {
            into.Fill(points[0]);
            return;
        }

        var step = total / (into.Length - 1);
        into[0] = points[0];
        var written = 1;
        var travelled = 0f;
        var target = step;
        for (var i = 1; i < points.Length && written < into.Length - 1; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            var segment = Vector2.Distance(from, to);
            while (segment > 0 && travelled + segment >= target && written < into.Length - 1)
            {
                var t = (target - travelled) / segment;
                into[written++] = Vector2.Lerp(from, to, t);
                target += step;
            }

            travelled += segment;
        }

        while (written < into.Length)
        {
            into[written++] = points[^1];
        }
    }

    private static void Normalize(ReadOnlySpan<Vector2> points, Span<Vector2> into)
    {
        var min = points[0];
        var max = points[0];
        var sum = Vector2.Zero;
        foreach (var point in points)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
            sum += point;
        }

        var centroid = sum / points.Length;
        var size = Math.Max(max.X - min.X, max.Y - min.Y);
        var scale = size > 1e-3f ? 1 / size : 0;
        for (var i = 0; i < points.Length; i++)
        {
            into[i] = (points[i] - centroid) * scale;
        }
    }
}
