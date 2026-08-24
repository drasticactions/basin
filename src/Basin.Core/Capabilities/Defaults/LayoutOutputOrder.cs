namespace Basin.Capabilities.Defaults;

public sealed class LayoutOutputOrder : IOutputOrder
{
    private readonly IOutputSet? _outputs;
    private readonly OutputLayout? _layout;
    private readonly Dictionary<IOutput, Action<OutputStateFields>> _watched = [];

    public LayoutOutputOrder(IOutputSet? outputs, OutputLayout? layout)
    {
        _outputs = outputs;
        _layout = layout;
        if (outputs is not null)
        {
            outputs.Changed += OnSetChanged;
        }

        if (layout is not null)
        {
            layout.Changed += OnLayoutChanged;
        }

        Watch();
    }

    public event Action? Changed;

    public int Enumerate(Span<IOutput> outputs)
    {
        var set = _outputs?.Outputs;
        if (set is null)
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < set.Count; i++)
        {
            var output = set[i];
            if (!Ordered(output))
            {
                continue;
            }

            if (count == outputs.Length)
            {
                return -1;
            }

            outputs[count++] = output;
        }

        for (var i = 1; i < count; i++)
        {
            var current = outputs[i];
            var j = i - 1;
            while (j >= 0 && Compare(outputs[j], current) > 0)
            {
                outputs[j + 1] = outputs[j];
                j--;
            }

            outputs[j + 1] = current;
        }

        return count;
    }

    private bool Ordered(IOutput output) =>
        output.Enabled && (_layout is null || _layout.Contains(output));

    private int Compare(IOutput left, IOutput right)
    {
        if (_layout is not null)
        {
            var a = _layout.BoxOf(left);
            var b = _layout.BoxOf(right);
            if (a.X != b.X)
            {
                return a.X.CompareTo(b.X);
            }

            if (a.Y != b.Y)
            {
                return a.Y.CompareTo(b.Y);
            }
        }

        return string.CompareOrdinal(left.Name, right.Name);
    }

    private void OnSetChanged()
    {
        Watch();
        Changed?.Invoke();
    }

    private void OnLayoutChanged() => Changed?.Invoke();

    private void OnOutputCommitted(OutputStateFields fields)
    {
        if ((fields & OutputStateFields.Enabled) != 0)
        {
            Changed?.Invoke();
        }
    }

    private void Watch()
    {
        var set = _outputs?.Outputs;
        if (set is null)
        {
            return;
        }

        foreach (var (output, handler) in _watched)
        {
            if (!set.Contains(output))
            {
                output.Committed -= handler;
                _watched.Remove(output);
            }
        }

        for (var i = 0; i < set.Count; i++)
        {
            var output = set[i];
            if (_watched.ContainsKey(output))
            {
                continue;
            }

            void OnCommitted(OutputStateFields fields) => OnOutputCommitted(fields);
            _watched[output] = OnCommitted;
            output.Committed += OnCommitted;
        }
    }
}
