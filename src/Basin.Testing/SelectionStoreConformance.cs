using Basin.Capabilities;
using Xunit;

namespace Basin.Testing;

public abstract class SelectionStoreConformance
{
    protected abstract ISelectionStore Create();

    protected static DataSource Source(params string[] mimeTypes) =>
        new([.. mimeTypes], (_, fd) => fd.Close());

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

    [Fact]
    public void A_span_that_is_too_small_reports_minus_one_rather_than_truncating()
    {
        var store = Create();
        store.SetSelection(SelectionKind.Clipboard, Source("a", "b", "c"), SelectionSerial.Unchecked);

        Assert.Equal(-1, store.GetOffer(SelectionKind.Clipboard, new string[2]));
    }

    [Fact]
    public void The_two_selections_are_separate_channels()
    {
        var store = Create();
        store.SetSelection(SelectionKind.Clipboard, Source("text/plain"), SelectionSerial.Unchecked);

        Assert.Equal(0, store.GetOffer(SelectionKind.Primary, new string[4]));
    }

    [Fact]
    public void Setting_a_selection_announces_the_change()
    {
        var store = Create();
        var announced = new List<SelectionKind>();
        store.SelectionChanged += announced.Add;

        store.SetSelection(SelectionKind.Clipboard, Source("text/plain"), SelectionSerial.Unchecked);

        Assert.Contains(SelectionKind.Clipboard, announced);
    }

    [Fact]
    public void Receiving_from_an_empty_selection_is_declined()
    {
        var store = Create();
        Assert.False(store.Receive(SelectionKind.Clipboard, "text/plain", new ClientFd(-1, null)));
    }
}
