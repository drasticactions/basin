using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Basin.Ipc;

/// <summary>
/// The declared events and their subscribers. A library source attaches to its capability only while its event has a subscriber.
/// </summary>
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

    /// <summary>
    /// Gets the declared event names in ordinal order.
    /// </summary>
    public IReadOnlyList<string> Names => SortedNames;

    internal string[] SortedNames => _sorted ??= [.. _topics.Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    /// Gets the number of library event sources that watch their capability now.
    /// </summary>
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

    /// <summary>
    /// Declares a consumer event so that clients can subscribe to it.
    /// </summary>
    /// <param name="name">The event name, <c>namespace/verb</c> in <c>[a-z0-9-]</c>.</param>
    /// <exception cref="ArgumentException">The name is not valid, or its namespace belongs to the library.</exception>
    /// <exception cref="InvalidOperationException">The name is already declared.</exception>
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

    /// <summary>
    /// Tells whether an event is declared.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <returns><see langword="true"/> when the event is declared.</returns>
    public bool IsDeclared(string name) => _topics.ContainsKey(name);

    /// <summary>
    /// Finds the oldest subscriber of an event.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <returns>The subscriber, or <see langword="null"/> when the event has none or is not declared.</returns>
    public IIpcEventSubscriber? FirstSubscriber(string name) =>
        _topics.TryGetValue(name, out var topic) && topic.Subscribers.Count > 0 ? topic.Subscribers[0] : null;

    /// <summary>
    /// Tells whether an event has a subscriber. An emitter can check it to skip work.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <returns><see langword="false"/> when the event has no subscriber or is not declared.</returns>
    public bool HasSubscribers(string name) => _topics.TryGetValue(name, out var topic) && topic.Subscribers.Count > 0;

    /// <summary>
    /// Subscribes to an event with no options.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="subscriber">The subscriber.</param>
    /// <returns><see langword="false"/> when the event is not declared or its source refuses.</returns>
    public bool Subscribe(string name, IIpcEventSubscriber subscriber) => TrySubscribe(name, subscriber, null, out _);

    /// <summary>
    /// Checks a subscription before it is made.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="options">The subscription's options, or <see langword="null"/>.</param>
    /// <returns>The text of the refusal, or <see langword="null"/> when the subscription is valid.</returns>
    public string? Validate(string name, JsonElement? options) =>
        !_topics.TryGetValue(name, out var topic) ? $"no event '{name}' in this session" : topic.Source?.Validate(name, options);

    /// <summary>
    /// Subscribes to an event. A second subscription by the same subscriber replaces its options and keeps its place in the order.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="subscriber">The subscriber.</param>
    /// <param name="options">The subscription's options, or <see langword="null"/>. The bus keeps a copy.</param>
    /// <param name="error">The text of the refusal, or <see langword="null"/> on success.</param>
    /// <returns><see langword="false"/> when the event is not declared or its source refuses the options.</returns>
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

    /// <summary>
    /// Lists the subscribers of an event, oldest first.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <returns>The live list of subscribers. It is empty when the event is not declared.</returns>
    public IReadOnlyList<IIpcEventSubscriber> Subscribers(string name) =>
        _topics.TryGetValue(name, out var topic) ? topic.Subscribers : [];

    /// <summary>
    /// Finds the options that a subscriber gave for an event.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="subscriber">The subscriber.</param>
    /// <returns>The options, or <see langword="null"/> when the subscriber gave none.</returns>
    public JsonElement? OptionsOf(string name, IIpcEventSubscriber subscriber) =>
        _topics.TryGetValue(name, out var topic) && topic.Options is { } options && options.TryGetValue(subscriber, out var found)
            ? found
            : null;

    /// <summary>
    /// Sets how the line front prints an event. A second call for the same event replaces the first.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="format">A function from the event's <c>data</c> JSON to the text to print, or to <see langword="null"/> to print nothing.</param>
    public void AddLineFormatter(string name, Func<ReadOnlySpan<byte>, string?> format)
    {
        ArgumentNullException.ThrowIfNull(format);
        _lineFormatters[name] = format;
    }

    internal Func<ReadOnlySpan<byte>, string?>? LineFormatter(string name) => _lineFormatters.GetValueOrDefault(name);

    /// <summary>
    /// Removes a subscription. It does nothing when the subscriber is not subscribed.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="subscriber">The subscriber.</param>
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

    /// <summary>
    /// Removes every subscription of a subscriber, for example when its connection closes.
    /// </summary>
    /// <param name="subscriber">The subscriber.</param>
    public void UnsubscribeAll(IIpcEventSubscriber subscriber)
    {
        foreach (var name in Names)
        {
            Unsubscribe(name, subscriber);
        }
    }

    /// <summary>
    /// Starts to write the <c>data</c> of an event. <see cref="EndEvent"/> sends it. Only one event can be written at a time.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <returns>The writer of the <c>data</c> JSON. It is valid until <see cref="EndEvent"/>.</returns>
    /// <exception cref="InvalidOperationException">Another event is still being written.</exception>
    /// <exception cref="ArgumentException">The event is not declared.</exception>
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

    /// <summary>
    /// Serializes an event and sends it to every subscriber. It does nothing when the event has no subscriber.
    /// </summary>
    /// <typeparam name="T">The event contract type.</typeparam>
    /// <param name="name">The event name.</param>
    /// <param name="value">The event data.</param>
    /// <param name="info">The source-generated metadata of <typeparamref name="T"/>.</param>
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

    /// <summary>
    /// Finishes the event that <see cref="BeginEvent"/> started and sends it to every subscriber.
    /// </summary>
    /// <exception cref="InvalidOperationException">No event is being written.</exception>
    public void EndEvent()
    {
        var name = _emitting ?? throw new InvalidOperationException("no event is being written");
        _emitting = null;
        _dataWriter!.Flush();
        Emit(name, _data!.Length == 0 ? "{}"u8 : _data.Written);
    }

    /// <summary>
    /// Serializes an event and sends it to one subscriber only, whether it is subscribed or not.
    /// </summary>
    /// <typeparam name="T">The event contract type.</typeparam>
    /// <param name="name">The event name, which must be declared.</param>
    /// <param name="subscriber">The subscriber that receives the event.</param>
    /// <param name="value">The event data.</param>
    /// <param name="info">The source-generated metadata of <typeparamref name="T"/>.</param>
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

    /// <summary>
    /// Sends an event whose <c>data</c> is already JSON to every subscriber. It does nothing when the event has no subscriber or is not declared.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="data">The <c>data</c> JSON. The bus does not validate it.</param>
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

    /// <summary>
    /// Removes every subscriber and detaches every library source.
    /// </summary>
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
