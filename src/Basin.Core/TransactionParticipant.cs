using Basin.Diagnostics;

namespace Basin;

public readonly struct TransactionParticipant
{
    private readonly Transaction? _transaction;
    private readonly int _index;

    internal TransactionParticipant(Transaction transaction, int index)
    {
        _transaction = transaction;
        _index = index;
    }

    public bool IsEmpty => _transaction is null;

    public Transaction? Transaction => _transaction;

    public void Ready() => _transaction?.ReportReady(_index);

    public void Abandon() => _transaction?.ReportReady(_index);
}
