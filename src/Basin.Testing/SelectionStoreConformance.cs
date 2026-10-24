using Basin.Capabilities;
using Xunit;

namespace Basin.Testing;

/// <summary>
/// The conformance rows every <see cref="ISelectionStore"/> implementation must pass.
/// A test class derives from it and supplies the store.
/// </summary>
public abstract class SelectionStoreConformance
{
    /// <summary>
    /// Creates an empty store under test. Each row calls it once.
    /// </summary>
    /// <returns>A new store with no selection set.</returns>
    protected abstract ISelectionStore Create();

    /// <summary>
    /// Creates a data source that offers the given MIME types and closes every receive fd without writing.
    /// </summary>
    /// <param name="mimeTypes">The MIME types to offer.</param>
    /// <returns>A source for <see cref="ISelectionStore.SetSelection"/>.</returns>
    protected static DataSource Source(params string[] mimeTypes) =>
        new([.. mimeTypes], (_, fd) => fd.Close());

    /// <summary>
    /// Checks that a new store offers no types and has no current source for either selection.
    /// </summary>
    [Fact]
    public void An_empty_store_offers_nothing_and_answers_null()
    {
        var store = Create();
        var types = new string[8];

        Assert.Equal(0, store.GetOffer(SelectionKind.Clipboard, types));
        Assert.Equal(0, store.GetOffer(SelectionKind.Primary, types));
        Assert.Null(store.Current(SelectionKind.Clipboard));
        Assert.Null(store.Current(SelectionKind.Primary));
    }

    /// <summary>
    /// Checks that a set selection offers its MIME types back.
    /// </summary>
    [Fact]
    public void A_set_selection_is_offered_back()
    {
        var store = Create();
        Assert.True(store.SetSelection(SelectionKind.Clipboard, Source("text/plain", "text/html"), SelectionSerial.Unchecked));

        var types = new string[8];
        Assert.Equal(2, store.GetOffer(SelectionKind.Clipboard, types));
        Assert.Contains("text/plain", types[..2]);
        Assert.NotNull(store.Current(SelectionKind.Clipboard));
    }

    /// <summary>
    /// Checks that a span too small for the offer gives -1 and no partial list.
    /// </summary>
    [Fact]
    public void A_span_that_is_too_small_reports_minus_one_rather_than_truncating()
    {
        var store = Create();
        store.SetSelection(SelectionKind.Clipboard, Source("a", "b", "c"), SelectionSerial.Unchecked);

        Assert.Equal(-1, store.GetOffer(SelectionKind.Clipboard, new string[2]));
    }

    /// <summary>
    /// Checks that the clipboard selection does not show in the primary selection.
    /// </summary>
    [Fact]
    public void The_two_selections_are_separate_channels()
    {
        var store = Create();
        store.SetSelection(SelectionKind.Clipboard, Source("text/plain"), SelectionSerial.Unchecked);

        Assert.Equal(0, store.GetOffer(SelectionKind.Primary, new string[4]));
    }

    /// <summary>
    /// Checks that setting a selection raises <see cref="ISelectionStore.SelectionChanged"/>.
    /// </summary>
    [Fact]
    public void Setting_a_selection_announces_the_change()
    {
        var store = Create();
        var announced = new List<SelectionKind>();
        store.SelectionChanged += announced.Add;

        store.SetSelection(SelectionKind.Clipboard, Source("text/plain"), SelectionSerial.Unchecked);

        Assert.Contains(SelectionKind.Clipboard, announced);
    }

    /// <summary>
    /// Checks that a receive from an empty selection returns false.
    /// </summary>
    [Fact]
    public void Receiving_from_an_empty_selection_is_declined()
    {
        var store = Create();
        Assert.False(store.Receive(SelectionKind.Clipboard, "text/plain", new ClientFd(-1, null)));
    }
}
