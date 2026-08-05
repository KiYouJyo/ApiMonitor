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

    public static BalanceQueryResult Failure(
        BalanceErrorKind kind,
        string message,
        int? httpStatusCode,
        string? providerErrorCode = null) =>
        new(false, null, new BalanceQueryError(kind, message, httpStatusCode, providerErrorCode));
}

public sealed class BalanceQueryError
{
    public BalanceErrorKind Kind { get; }

    public string Message { get; }

    /// <summary>HTTP 状态码（可选；安全展示，不含请求 URI）。</summary>
    public int? HttpStatusCode { get; }

    /// <summary>官方数值错误码（可选；如高德 10001、百度 4、腾讯 121）。</summary>
    public string? ProviderErrorCode { get; }

    public BalanceQueryError(
        BalanceErrorKind kind,
        string message,
        int? httpStatusCode = null,
        string? providerErrorCode = null)
    {
        Kind = kind;
        Message = message;
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }
}

public enum BalanceErrorKind
{
    Unknown,
    Network,
    Timeout,
    Unauthorized,
    PaymentRequired,
    Forbidden,
    RateLimited,
    ServerError,
    EmptyContent,
    InvalidJson,
    InvalidResponse,
    Busy,
    NotSupported,
    MissingCredential,
    /// <summary>非敏感配置缺失（如 xAI Team ID）。</summary>
    ConfigurationMissing,
    AccountNotFound,
    LocalData,
    // ------------------------------------------------------------------
    // v0.9.0：地理/GIS 专用错误分类（与官方状态码映射，禁止猜测语义）。
    // ------------------------------------------------------------------
    TlsFailure,
    CredentialInvalid,
    KeyTypeMismatch,
    IpWhitelistDenied,
    RefererDomainDenied,
    SignatureInvalid,
    ServiceNotEnabled,
    PermissionDenied,
    QuotaExceeded,
    NotFound,
    InvalidXml,
    TooLarge,
    EmptyCatalog,
    ExpectedServiceMissing,
    ExpectedLayerMissing,
    RedirectBlocked,
    ProtocolViolation,
}
