namespace ApiMonitor.Models;

/// <summary>
/// Provider 查询结果：要么成功返回快照，要么返回可理解的错误分类。
/// </summary>
public sealed class BalanceQueryResult
{
    public bool IsSuccess { get; }

    public BalanceSnapshot? Snapshot { get; }

    public BalanceQueryError? Error { get; }

    private BalanceQueryResult(bool isSuccess, BalanceSnapshot? snapshot, BalanceQueryError? error)
    {
        IsSuccess = isSuccess;
        Snapshot = snapshot;
        Error = error;
    }

    public static BalanceQueryResult Success(BalanceSnapshot snapshot) =>
        new(true, snapshot, null);

    public static BalanceQueryResult Failure(BalanceErrorKind kind, string message) =>
        new(false, null, new BalanceQueryError(kind, message));
}

public sealed class BalanceQueryError
{
    public BalanceErrorKind Kind { get; }

    public string Message { get; }

    public BalanceQueryError(BalanceErrorKind kind, string message)
    {
        Kind = kind;
        Message = message;
    }
}

public enum BalanceErrorKind
{
    Unknown,
    Network,
    Timeout,
    Unauthorized,
    Forbidden,
    RateLimited,
    ServerError,
    EmptyContent,
    InvalidJson,
    Busy,
    NotSupported,
    MissingCredential,
    AccountNotFound,
    LocalData,
}
