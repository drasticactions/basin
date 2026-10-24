using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// One participant of a <see cref="Basin.Transaction"/>. The default value is empty, and its calls do nothing.
/// </summary>
public readonly struct TransactionParticipant
{
    private readonly Transaction? _transaction;
    private readonly int _index;

    internal TransactionParticipant(Transaction transaction, int index)
    {
        _transaction = transaction;
        _index = index;
    }

    /// <summary>
    /// Gets a value indicating whether this participant belongs to no transaction.
    /// </summary>
    public bool IsEmpty => _transaction is null;

    /// <summary>
    /// Gets the transaction that this participant joined, or <see langword="null"/> for an empty participant.
    /// </summary>
    public Transaction? Transaction => _transaction;

    /// <summary>
    /// Reports that this participant is ready. A second report from the same participant, or a report after completion, does nothing.
    /// </summary>
    public void Ready() => _transaction?.ReportReady(_index);

    /// <summary>
    /// Withdraws this participant so that the transaction does not wait for it. It has the same effect as <see cref="Ready"/>.
    /// </summary>
    public void Abandon() => _transaction?.ReportReady(_index);
}
