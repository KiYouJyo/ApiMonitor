namespace ApiMonitor.Models;

public sealed class AccountRefreshStartedEventArgs : EventArgs
{
    public required string AccountId { get; init; }

    public required BalanceQuerySource Source { get; init; }
}

public sealed class AccountRefreshCompletedEventArgs : EventArgs
{
    public required string AccountId { get; init; }

    public required BalanceQueryResult Result { get; init; }

    public required BalanceQuerySource Source { get; init; }
}
