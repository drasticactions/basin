using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Basin.Ipc;

public sealed class IpcEventBus : IDisposable
{
    private readonly Dictionary<string, Topic> _topics = new(StringComparer.Ordinal);
    private readonly List<IpcEventSource> _sources = [];
    private readonly Dictionary<string, Func<ReadOnlySpan<byte>, string?>> _lineFormatters = new(StringComparer.Ordinal);
    private IpcByteBuffer? _data;
    private IpcByteBuffer? _message;
    private Utf8JsonWriter? _dataWriter;
    private Utf8JsonWriter? _messageWriter;
    private string? _emitting;
    private string[]? _sorted;

    public IReadOnlyList<string> Names => SortedNames;

    internal string[] SortedNames => _sorted ??= [.. _topics.Keys.Order(StringComparer.Ordinal)];

    public int AttachedSources
    {
        get
        {
            var count = 0;
            foreach (var source in _sources)
            {
                if (source.IsAttached)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public void Declare(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var reserved in IpcEventNames.ReservedNamespaces)
        {
            if (IpcProtocol.NamespaceOf(name).SequenceEqual(reserved))
            {
                throw new ArgumentException($"'{name}' is in an event namespace the library owns", nameof(name));
            }
        }

        Add(name, null);
    }

    public bool IsDeclared(string name) => _topics.ContainsKey(name);

    public IIpcEventSubscriber? FirstSubscriber(string name) =>
        _topics.TryGetValue(name, out var topic) && topic.Subscribers.Count > 0 ? topic.Subscribers[0] : null;

    public bool HasSubscribers(string name) => _topics.TryGetValue(name, out var topic) && topic.Subscribers.Count > 0;

    public bool Subscribe(string name, IIpcEventSubscriber subscriber) => TrySubscribe(name, subscriber, null, out _);

    public string? Validate(string name, JsonElement? options) =>
        !_topics.TryGetValue(name, out var topic) ? $"no event '{name}' in this session" : topic.Source?.Validate(name, options);

    public bool TrySubscribe(string name, IIpcEventSubscriber subscriber, JsonElement? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        error = Validate(name, options);
        if (error is not null || !_topics.TryGetValue(name, out var topic))
        {
            return false;
        }

        if (options is { } given)
        {
            (topic.Options ??= [])[subscriber] = given.Clone();
        }
        else
        {
            topic.Options?.Remove(subscriber);
        }

        if (!topic.Subscribers.Contains(subscriber))
        {
            topic.Subscribers.Add(subscriber);
            if (topic.Source is { } source && ++source.Demand == 1)
            {
                source.SetAttached(true);
            }
        }

        topic.Source?.SubscribersChanged(name);
        return true;
    }

    public IReadOnlyList<IIpcEventSubscriber> Subscribers(string name) =>
        _topics.TryGetValue(name, out var topic) ? topic.Subscribers : [];

    public JsonElement? OptionsOf(string name, IIpcEventSubscriber subscriber) =>
        _topics.TryGetValue(name, out var topic) && topic.Options is { } options && options.TryGetValue(subscriber, out var found)
            ? found
            : null;

    public void AddLineFormatter(string name, Func<ReadOnlySpan<byte>, string?> format)
    {
        ArgumentNullException.ThrowIfNull(format);
        _lineFormatters[name] = format;
    }

    internal Func<ReadOnlySpan<byte>, string?>? LineFormatter(string name) => _lineFormatters.GetValueOrDefault(name);

    public void Unsubscribe(string name, IIpcEventSubscriber subscriber)
    {
        if (!_topics.TryGetValue(name, out var topic) || !topic.Subscribers.Remove(subscriber))
        {
            return;
        }

        topic.Options?.Remove(subscriber);
        topic.Source?.SubscribersChanged(name);
        if (topic.Source is { } source && --source.Demand == 0)
        {
            source.SetAttached(false);
        }
    }

    public void UnsubscribeAll(IIpcEventSubscriber subscriber)
    {
        foreach (var name in Names)
        {
            Unsubscribe(name, subscriber);
        }
    }

    public Utf8JsonWriter BeginEvent(string name)
    {
        if (_emitting is not null)
        {
            throw new InvalidOperationException($"'{_emitting}' is still being written");
        }

        if (!_topics.ContainsKey(name))
        {
            throw new ArgumentException($"'{name}' is not a declared event", nameof(name));
        }

        _emitting = name;
        _data ??= new IpcByteBuffer();
        _dataWriter ??= new Utf8JsonWriter(_data, WriterOptions());
        _data.Clear();
        _dataWriter.Reset(_data);
        return _dataWriter;
    }

    public void Emit<T>(string name, T value, JsonTypeInfo<T> info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!HasSubscribers(name))
        {
            return;
        }

        JsonSerializer.Serialize(BeginEvent(name), value, info);
        EndEvent();
    }

    public void EndEvent()
    {
        var name = _emitting ?? throw new InvalidOperationException("no event is being written");
        _emitting = null;
        _dataWriter!.Flush();
        Emit(name, _data!.Length == 0 ? "{}"u8 : _data.Written);
    }

    public void EmitTo<T>(string name, IIpcEventSubscriber subscriber, T value, JsonTypeInfo<T> info)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(info);
        JsonSerializer.Serialize(BeginEvent(name), value, info);
        _emitting = null;
        _dataWriter!.Flush();
        Envelope(name, _data!.Length == 0 ? "{}"u8 : _data.Written);
        subscriber.OnEvent(name, _message!.Written);
    }

    public void Emit(string name, ReadOnlySpan<byte> data)
    {
        if (!_topics.TryGetValue(name, out var topic) || topic.Subscribers.Count == 0)
        {
            return;
        }

        Envelope(name, data);
        var subscribers = topic.Subscribers;
        for (var i = subscribers.Count - 1; i >= 0; i--)
        {
            if (i < subscribers.Count)
            {
                subscribers[i].OnEvent(name, _message!.Written);
            }
        }
    }

    private void Envelope(string name, ReadOnlySpan<byte> data)
    {
        _message ??= new IpcByteBuffer();
        _messageWriter ??= new Utf8JsonWriter(_message, WriterOptions());
        _message.Clear();
        _messageWriter.Reset(_message);
        _messageWriter.WriteStartObject();
        _messageWriter.WriteString("event"u8, name);
        _messageWriter.WritePropertyName("data"u8);
        _messageWriter.WriteRawValue(data, skipInputValidation: true);
        _messageWriter.WriteEndObject();
        _messageWriter.Flush();
    }

    public void Dispose()
    {
        foreach (var topic in _topics.Values)
        {
            topic.Subscribers.Clear();
        }

        foreach (var source in _sources)
        {
            source.Demand = 0;
            source.SetAttached(false);
        }

        _dataWriter?.Dispose();
        _messageWriter?.Dispose();
    }

    private static JsonWriterOptions WriterOptions() => new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal void DeclareLibrary(string name) => Add(name, null);

    internal void DeclareLibrary(string name, IpcEventSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_sources.Contains(source))
        {
            _sources.Add(source);
        }

        Add(name, source);
    }

    private void Add(string name, IpcEventSource? source)
    {
        if (!IpcProtocol.IsValidName(name))
        {
            throw new ArgumentException($"'{name}' is not namespace/verb in [a-z0-9-]", nameof(name));
        }

        if (!_topics.TryAdd(name, new Topic(source)))
        {
            throw new InvalidOperationException($"'{name}' is already declared");
        }

        _sorted = null;
    }

    private sealed class Topic(IpcEventSource? source)
    {
        public IpcEventSource? Source { get; } = source;

        public List<IIpcEventSubscriber> Subscribers { get; } = [];

        public Dictionary<IIpcEventSubscriber, JsonElement>? Options { get; set; }
    }
}
