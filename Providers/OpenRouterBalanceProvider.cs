using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Providers.Dto;
using ApiMonitor.Services;

namespace ApiMonitor.Providers;

/// <summary>
/// OpenRouter 余额/额度查询实现，支持两种凭据模式：
///   api-key（普通 API Key）：GET https://openrouter.ai/api/v1/key，解析密钥额度与周期使用量；
///   management-key（Management Key）：GET https://openrouter.ai/api/v1/credits，计算账户剩余 Credits。
/// 凭据模式保存在账户非敏感设置（ApiAccount.CredentialMode）中，
/// 密钥本身只进 ISecretStore；绝不调用模型推理接口，绝不产生模型调用费用。
/// </summary>
public sealed class OpenRouterBalanceProvider : IApiBalanceProvider
{
    public const string ProviderId = "openrouter";
    public const string DisplayName = "OpenRouter";

    public const string ApiKeyMode = "api-key";
    public const string ManagementKeyMode = "management-key";

    private const string KeyEndpoint = "https://openrouter.ai/api/v1/key";
    private const string CreditsEndpoint = "https://openrouter.ai/api/v1/credits";

    private readonly IHttpRequestService _http;
    private readonly AppLog? _log;

    public ProviderInfo Info { get; } = new(
        ProviderId,
        DisplayName,
        L10n.Get("Provider.OpenRouterDescription"),
        SupportsAccountBalance: true,
        SupportsKeyQuota: true,
        SupportedMetricKinds: new[]
        {
            BalanceMetricKind.PlatformCredits,
            BalanceMetricKind.KeyQuota,
            BalanceMetricKind.Usage,
        },
        CredentialOptions: new[]
        {
            new ProviderCredentialOption(
                ApiKeyMode,
                L10n.Get("Provider.OpenRouterApiKeyMode"),
                L10n.Get("Provider.OpenRouterApiKeyHint"),
                IsDefault: true),
            new ProviderCredentialOption(
                ManagementKeyMode,
                "Management Key",
                L10n.Get("Provider.OpenRouterMgmtHint"),
                IsDefault: false),
        },
        ApiKeyInputHint: L10n.Get("Provider.OpenRouterKeyInputHint"),
        HelpUrl: "https://openrouter.ai/settings/keys",
        SupportsTestConnection: true,
        DefaultBaseUrl: "https://openrouter.ai",
        ConfigFields: Array.Empty<ProviderConfigField>(),
        // 账户主指标：Management Key 模式为剩余 Credits；普通 API Key 模式的主指标
        // 为密钥剩余额度（openrouter:key:quota-remaining），由快照内选择器按模式取用。
        PrimaryMetricId: "openrouter:credits:remaining",
        Currency: null,
        SupportsMultiCurrency: false,
        SupportsBreakdown: true,
        SupportsCredentialValidation: true,
        AllowCustomEndpoint: false);

    string IApiBalanceProvider.ProviderId => ProviderId;

    string IApiBalanceProvider.DisplayName => DisplayName;

    public OpenRouterBalanceProvider(IHttpRequestService http, AppLog? log = null)
    {
        _http = http;
        _log = log;
    }

    public async Task<BalanceQueryResult> QueryBalanceAsync(
        ApiAccount account,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.MissingCredential,
                L10n.Get("Provider.ErrorNoKey"));
        }

        // 凭据模式是用户明确选择的账户设置；null 按默认普通 Key 处理。
        bool useManagementKey = string.Equals(
            account.CredentialMode,
            ManagementKeyMode,
            StringComparison.OrdinalIgnoreCase);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                useManagementKey ? CreditsEndpoint : KeyEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return await HandleResponseAsync(account, response, useManagementKey, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Timeout,
                L10n.Get("Provider.ErrorTimeout"));
        }
        catch (HttpRequestException)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Network,
                L10n.Get("Provider.ErrorNetwork"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"OpenRouter 查询发生意外错误: {ex.GetType().Name}");
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                L10n.Get("Provider.ErrorUnexpected"));
        }
    }

    private static async Task<BalanceQueryResult> HandleResponseAsync(
        ApiAccount account,
        HttpResponseMessage response,
        bool useManagementKey,
        CancellationToken cancellationToken)
    {
        string endpointLabel = useManagementKey ? "Credits" : "Key";

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unauthorized,
                L10n.Get("Provider.Error401"));
        }

        if (response.StatusCode == (HttpStatusCode)402)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.PaymentRequired,
                L10n.Get("Provider.Error402"));
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return useManagementKey
                ? BalanceQueryResult.Failure(
                    BalanceErrorKind.Forbidden,
                    L10n.Get("Provider.Error403Credits"))
                : BalanceQueryResult.Failure(
                    BalanceErrorKind.Forbidden,
                    L10n.Get("Provider.Error403"));
        }

        if (response.StatusCode == (HttpStatusCode)429)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.RateLimited,
                L10n.Get("Provider.Error429"));
        }

        if ((int)response.StatusCode >= 500)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.ServerError,
                L10n.Format("Provider.ErrorServiceUnavailable", (int)response.StatusCode));
        }

        if (!response.IsSuccessStatusCode)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                L10n.Format("Provider.ErrorUnexpectedStatus", (int)response.StatusCode));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.EmptyContent,
                L10n.Get("Provider.ErrorEmptyContent"));
        }

        return useManagementKey
            ? ParseCreditsResponse(account, body)
            : ParseKeyResponse(account, body);
    }

    private static BalanceQueryResult ParseKeyResponse(ApiAccount account, string body)
    {
        OpenRouterKeyResponse? dto;
        try
        {
            dto = JsonSerializer.Deserialize<OpenRouterKeyResponse>(body);
        }
        catch (JsonException)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidJson,
                L10n.Get("Provider.ErrorInvalidJson"));
        }

        var data = dto?.Data;
        if (data is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorMissingData"));
        }

        decimal? limit = ReadDecimal(data.Limit);
        decimal? limitRemaining = ReadDecimal(data.LimitRemaining);
        decimal? usage = ReadDecimal(data.Usage);
        decimal? usageDaily = ReadDecimal(data.UsageDaily);
        decimal? usageWeekly = ReadDecimal(data.UsageWeekly);
        decimal? usageMonthly = ReadDecimal(data.UsageMonthly);
        decimal? byokUsage = ReadDecimal(data.ByokUsage);

        // 所有数值字段都缺失时视为响应结构不完整；label 缺失不影响。
        if (limit is null
            && limitRemaining is null
            && usage is null
            && usageDaily is null
            && usageWeekly is null
            && usageMonthly is null
            && byokUsage is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorMissingField"));
        }

        var metrics = new List<BalanceMetric>
        {
            new()
            {
                MetricId = "openrouter:key:quota-remaining",
                DisplayName = "密钥剩余额度",
                Unit = "credits",
                Kind = BalanceMetricKind.KeyQuota,
                AvailableAmount = limitRemaining,
                TotalAmount = limit,
                // limit_remaining=null 表示未设置密钥额度或额度不受该字段限制：
                // 显示为无限额度，绝不误触发低余额提醒，也绝不当成 0。
                IsThresholdSupported = limitRemaining is not null,
                IsUnlimited = limitRemaining is null,
            },
            new()
            {
                MetricId = "openrouter:key:quota-limit",
                DisplayName = "密钥额度上限",
                Unit = "credits",
                Kind = BalanceMetricKind.KeyQuota,
                TotalAmount = limit,
            },
        };

        AddUsageMetric(metrics, "openrouter:key:usage-total", "累计使用量", usage);
        AddUsageMetric(metrics, "openrouter:key:usage-daily", "今日使用量", usageDaily);
        AddUsageMetric(metrics, "openrouter:key:usage-weekly", "本周使用量", usageWeekly);
        AddUsageMetric(metrics, "openrouter:key:usage-monthly", "本月使用量", usageMonthly);
        AddUsageMetric(metrics, "openrouter:key:usage-byok", "BYOK 使用量", byokUsage);

        return BalanceQueryResult.Success(new BalanceSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            ProviderId = account.ProviderId,
            IsAvailable = true,
            RetrievedAt = DateTimeOffset.UtcNow,
            Metrics = metrics,
        });
    }

    private static BalanceQueryResult ParseCreditsResponse(ApiAccount account, string body)
    {
        OpenRouterCreditsResponse? dto;
        try
        {
            dto = JsonSerializer.Deserialize<OpenRouterCreditsResponse>(body);
        }
        catch (JsonException)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidJson,
                L10n.Get("Provider.ErrorInvalidJson"));
        }

        decimal? totalCredits = ReadDecimal(dto?.TotalCredits);
        decimal? totalUsage = ReadDecimal(dto?.TotalUsage);
        if (totalCredits is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorMissingTotalCredits"));
        }

        // remaining_credits = total_credits - total_usage；不要强制把负数钳制为 0。
        decimal remaining = totalCredits.Value - (totalUsage ?? 0m);

        var metrics = new List<BalanceMetric>
        {
            new()
            {
                MetricId = "openrouter:credits:remaining",
                DisplayName = "剩余 Credits",
                Unit = "credits",
                Kind = BalanceMetricKind.PlatformCredits,
                AvailableAmount = remaining,
                TotalAmount = totalCredits,
                UsedAmount = totalUsage,
                IsThresholdSupported = true,
            },
            new()
            {
                MetricId = "openrouter:credits:total",
                DisplayName = "累计充值 Credits",
                Unit = "credits",
                Kind = BalanceMetricKind.PlatformCredits,
                TotalAmount = totalCredits,
            },
            new()
            {
                MetricId = "openrouter:credits:usage",
                DisplayName = "累计使用 Credits",
                Unit = "credits",
                Kind = BalanceMetricKind.Usage,
                UsedAmount = totalUsage,
            },
        };

        return BalanceQueryResult.Success(new BalanceSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            ProviderId = account.ProviderId,
            IsAvailable = true,
            RetrievedAt = DateTimeOffset.UtcNow,
            Metrics = metrics,
        });
    }

    private static void AddUsageMetric(
        List<BalanceMetric> metrics,
        string metricId,
        string displayName,
        decimal? value)
    {
        if (value is null)
        {
            return;
        }

        metrics.Add(new BalanceMetric
        {
            MetricId = metricId,
            DisplayName = displayName,
            Unit = "credits",
            Kind = BalanceMetricKind.Usage,
            UsedAmount = value,
        });
    }

    private static decimal? ReadDecimal(JsonElement? element)
    {
        if (element is not { } e)
        {
            return null;
        }

        if (e.ValueKind == JsonValueKind.Number && e.TryGetDecimal(out var number))
        {
            return number;
        }

        if (e.ValueKind == JsonValueKind.String)
        {
            string? text = e.GetString();
            if (!string.IsNullOrWhiteSpace(text)
                && decimal.TryParse(
                    text,
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
