using System.Collections.Concurrent;
using ApiMonitor.Models;
using ApiMonitor.Providers;

namespace ApiMonitor.Services;

/// <summary>
/// 组合账户存储、凭据存储、快照/历史存储与 Provider 注册表的门面实现。
/// 手动与自动刷新共用同一账户级并发保护，历史写入与最新快照更新在同一次原子保存中完成。
/// </summary>
public sealed class AccountManager : IAccountManager
{
    private readonly IAccountStore _accountStore;
    private readonly IBalanceSnapshotStore _snapshotStore;
    private readonly ISecretStore _secretStore;
    private readonly ProviderRegistry _registry;
    private readonly AppLog _log;
    private readonly TimeProvider _time;

    private readonly List<ApiAccount> _accounts = new();
    private readonly Dictionary<string, AccountBalanceRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new(StringComparer.OrdinalIgnoreCase);

    private int _activeRefreshCount;

    public event EventHandler<AccountRefreshStartedEventArgs>? RefreshStarted;

    public event EventHandler<AccountRefreshCompletedEventArgs>? RefreshCompleted;

    public event EventHandler? AccountsChanged;

    public event EventHandler<AccountDeletedEventArgs>? AccountDeleted;

    public AccountManager(
        IAccountStore accountStore,
        IBalanceSnapshotStore snapshotStore,
        ISecretStore secretStore,
        ProviderRegistry registry,
        AppLog log,
        TimeProvider? time = null)
    {
        _accountStore = accountStore;
        _snapshotStore = snapshotStore;
        _secretStore = secretStore;
        _registry = registry;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyList<string> RecoveryMessages { get; private set; } = Array.Empty<string>();

    public bool HasActiveRefresh => Volatile.Read(ref _activeRefreshCount) > 0;

    public IReadOnlyList<ProviderInfo> Providers => _registry.Infos;

    private DateTimeOffset NowUtc => _time.GetUtcNow();

    public async Task<IReadOnlyList<ApiAccount>> LoadAsync(CancellationToken cancellationToken)
    {
        var accounts = await _accountStore.LoadAsync(cancellationToken);
        var records = await _snapshotStore.LoadAsync(cancellationToken);

        var messages = new List<string>(2);
        if (accounts.RecoveryMessage is { } accountMessage)
        {
            messages.Add(accountMessage);
        }

        if (records.RecoveryMessage is { } recordMessage)
        {
            messages.Add(recordMessage);
        }

        RecoveryMessages = messages;

        _accounts.Clear();
        _accounts.AddRange(accounts.Accounts);

        _records.Clear();
        foreach (var record in records.Records)
        {
            _records[record.AccountId] = record;
        }

        // v0.9.0：从 Credential Locker 读取各账户实际存在的凭据槽位，
        // 保证编辑对话框能显示“已有哪些凭据”（账户 JSON 只保存存在标志）。
        foreach (var account in _accounts)
        {
            var present = _secretStore.GetPresentSlots(account.AccountId);
            if (present.Count == 0)
            {
                if (account.HasCredential)
                {
                    present = new List<string> { CredentialSlots.Primary };
                }
            }

            if (present.Count > 0)
            {
                account.CredentialSlots = BuildSlotPresence(account.CredentialSlots, present);
            }
        }

        // v0.1.0 迁移/旧账户：为已启用但无下次刷新时间的账户按上次查询时间回填。
        bool settingsChanged = false;
        foreach (var account in _accounts)
        {
            var monitoring = account.Monitoring;
            if (monitoring.AutoRefreshEnabled && monitoring.NextRefreshAtUtc is null)
            {
                var last = _records.TryGetValue(account.AccountId, out var record)
                    ? record.LastQueryAttemptAt ?? record.LastQuerySuccessAt
                    : null;
                if (last is { } lastAt)
                {
                    monitoring.NextRefreshAtUtc = lastAt.AddMinutes(monitoring.RefreshIntervalMinutes);
                    settingsChanged = true;
                }
            }
        }

        if (settingsChanged)
        {
            await _accountStore.SaveAsync(_accounts, cancellationToken);
        }

        int pruned = await _snapshotStore.PruneAsync(cancellationToken);
        if (pruned > 0)
        {
            _log.Info($"历史记录保留策略清理 {pruned} 条。");
        }

        _log.Info($"已加载 {_accounts.Count} 个账户。");
        AccountsChanged?.Invoke(this, EventArgs.Empty);
        return _accounts;
    }

    public Task<IReadOnlyList<ApiAccount>> GetAllAccountsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ApiAccount>>(_accounts.ToList());
    }

    public Task<ApiAccount?> GetAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _accounts.FirstOrDefault(a =>
                string.Equals(a.AccountId, accountId, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<AccountBalanceRecord?> GetRecordAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _records.TryGetValue(accountId, out var record) ? record : null);
    }

    public Task<string?> GetApiKeyAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _secretStore.GetAsync(accountId, cancellationToken);
    }

    public async Task<BalanceQueryResult> TestConnectionAsync(
        string providerId,
        string? credentialMode,
        string? apiKey,
        string? accountId,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? providerConfig = null,
        IReadOnlyDictionary<string, string>? credentialSlots = null)
    {
        var provider = _registry.GetById(providerId);
        if (provider is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.NotSupported,
                L10n.Get("Account.ErrorUnsupportedProvider"));
        }

        string? key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        if (key is null && accountId is not null)
        {
            key = await _secretStore.GetAsync(accountId, cancellationToken);
        }

        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        if (key is not null)
        {
            slots[CredentialSlots.Primary] = key;
        }

        if (credentialSlots is not null)
        {
            foreach (var pair in credentialSlots)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    slots[pair.Key] = pair.Value.Trim();
                }
            }
        }

        if (slots.Count == 0)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.MissingCredential,
                L10n.Get("Account.ErrorKeyRequiredForTest"));
        }

        var probe = new ApiAccount
        {
            AccountId = accountId ?? "<test>",
            ProviderId = providerId,
            DisplayName = "<test>",
            HasCredential = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CredentialMode = credentialMode,
            ProviderConfig = providerConfig is { Count: > 0 }
                ? new Dictionary<string, string>(providerConfig, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal),
            CredentialSlots = BuildSlotPresence(
                new Dictionary<string, bool>(StringComparer.Ordinal),
                slots.Keys),
        };

        return await provider.QueryBalanceAsync(probe, slots, cancellationToken);
    }

    public async Task<ApiAccount> SaveAccountAsync(
        string? accountId,
        string providerId,
        string displayName,
        string? newApiKey,
        string? credentialMode,
        MonitoringSettings monitoring,
        CancellationToken cancellationToken,
        AccountNotificationSettings? notification = null,
        IReadOnlyDictionary<string, string>? providerConfig = null,
        IReadOnlyDictionary<string, string>? credentialSlots = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(L10n.Get("Account.ErrorNameRequired"), nameof(displayName));
        }

        if (_registry.GetById(providerId) is null)
        {
            throw new ArgumentException(L10n.Format("Account.ErrorUnsupportedProviderId", providerId), nameof(providerId));
        }

        // v0.9.0：按 Provider 分类校验自动刷新间隔（地图最短 1 小时，
        // 自托管 GIS 最短 5 分钟），防止 UI 之外传入更频繁的调度。
        var providerInfo = _registry.GetById(providerId)!.Info;
        var allowedIntervals = MonitoringIntervals.OptionsFor(providerInfo.EffectiveCategory);
        if (!allowedIntervals.Contains(monitoring.RefreshIntervalMinutes))
        {
            throw new ArgumentException(
                L10n.Format("Account.ErrorIntervalUnsupported", monitoring.RefreshIntervalMinutes),
                nameof(monitoring));
        }

        string id = accountId ?? Guid.NewGuid().ToString("N");
        var existing = _accounts.FirstOrDefault(a =>
            string.Equals(a.AccountId, id, StringComparison.OrdinalIgnoreCase));

        var now = NowUtc;
        bool hasCredential = existing?.HasCredential ?? false;
        var slotPresence = new Dictionary<string, bool>(
            existing?.CredentialSlots ?? new Dictionary<string, bool>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(newApiKey))
        {
            await _secretStore.SetAsync(id, newApiKey.Trim(), cancellationToken);
            slotPresence[CredentialSlots.Primary] = true;
            hasCredential = true;
        }

        if (credentialSlots is not null)
        {
            foreach (var pair in credentialSlots)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                await _secretStore.SetAsync(id, pair.Value.Trim(), cancellationToken, pair.Key);
                slotPresence[pair.Key] = true;
                hasCredential = true;
            }
        }

        var thresholds = monitoring.Thresholds
            .Where(t => !string.IsNullOrWhiteSpace(t.MetricId))
            .Select(t => new BalanceThresholdRule
            {
                MetricId = t.MetricId,
                DisplayName = t.DisplayName,
                Unit = t.Unit,
                IsEnabled = t.IsEnabled,
                ThresholdAmount = t.ThresholdAmount,
                CreatedAtUtc = t.CreatedAtUtc,
                UpdatedAtUtc = t.UpdatedAtUtc,
            })
            .ToList();

        DateTimeOffset? next = ComputeNextRefresh(existing, monitoring, now);

        var account = new ApiAccount
        {
            AccountId = id,
            ProviderId = providerId,
            DisplayName = displayName.Trim(),
            HasCredential = hasCredential,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now,
            CredentialMode = string.IsNullOrWhiteSpace(credentialMode) ? null : credentialMode.Trim(),
            ProviderConfig = providerConfig is { Count: > 0 }
                ? new Dictionary<string, string>(providerConfig, StringComparer.Ordinal)
                : providerConfig is null && existing is not null
                    ? new Dictionary<string, string>(existing.ProviderConfig, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal),
            CredentialSlots = slotPresence,
            Monitoring = new MonitoringSettings
            {
                AutoRefreshEnabled = monitoring.AutoRefreshEnabled,
                RefreshIntervalMinutes = monitoring.RefreshIntervalMinutes,
                NextRefreshAtUtc = next,
                Thresholds = thresholds,
            },
            Notification = notification ?? existing?.Notification ?? new AccountNotificationSettings(),
        };

        if (existing is null)
        {
            _accounts.Add(account);
            if (!_records.ContainsKey(id))
            {
                _records[id] = new AccountBalanceRecord
                {
                    AccountId = id,
                    ProviderId = providerId,
                };
            }
        }
        else
        {
            int index = _accounts.FindIndex(a =>
                string.Equals(a.AccountId, id, StringComparison.OrdinalIgnoreCase));
            _accounts[index] = account;
        }

        await PersistAsync(cancellationToken);
        _log.Info($"已保存账户 {account.AccountId}。");
        AccountsChanged?.Invoke(this, EventArgs.Empty);
        return account;
    }

    public async Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        await _secretStore.DeleteAsync(accountId, cancellationToken);

        _accounts.RemoveAll(a =>
            string.Equals(a.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
        _records.Remove(accountId);
        _refreshLocks.TryRemove(accountId, out _);

        await PersistAsync(cancellationToken);
        _log.Info($"已删除账户 {accountId} 及其凭据、余额快照与历史记录。");
        AccountDeleted?.Invoke(this, new AccountDeletedEventArgs { AccountId = accountId });
        AccountsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<BalanceQueryResult> RefreshAccountAsync(
        string accountId,
        BalanceQuerySource source,
        CancellationToken cancellationToken)
    {
        var semaphore = _refreshLocks.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(0, cancellationToken))
        {
            return BalanceQueryResult.Failure(BalanceErrorKind.Busy, L10n.Get("Account.ErrorBusy"));
        }

        try
        {
            var account = _accounts.FirstOrDefault(a =>
                string.Equals(a.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return BalanceQueryResult.Failure(BalanceErrorKind.AccountNotFound, L10n.Get("Account.ErrorNotFound"));
            }

            var provider = _registry.GetById(account.ProviderId);
            if (provider is null)
            {
                return BalanceQueryResult.Failure(
                    BalanceErrorKind.NotSupported,
                    L10n.Get("Account.ErrorProviderUnsupported"));
            }

            var record = _records.TryGetValue(accountId, out var existingRecord)
                ? existingRecord
                : new AccountBalanceRecord
                {
                    AccountId = accountId,
                    ProviderId = account.ProviderId,
                };

            record.LastQueryAttemptAt = NowUtc;

            BalanceQueryResult result;
            if (!account.HasCredential)
            {
                result = BalanceQueryResult.Failure(
                    BalanceErrorKind.MissingCredential,
                    L10n.Get("Account.ErrorNoSavedKey"));
            }
            else
            {
                var credentials = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string slot in account.CredentialSlots.Where(kv => kv.Value).Select(kv => kv.Key))
                {
                    string? value = await _secretStore.GetAsync(accountId, cancellationToken, slot);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        credentials[slot] = value;
                    }
                }

                if (credentials.Count == 0)
                {
                    result = BalanceQueryResult.Failure(
                        BalanceErrorKind.MissingCredential,
                        L10n.Get("Account.ErrorReadKeyFailed"));
                }
                else
                {
                    RefreshStarted?.Invoke(
                        this,
                        new AccountRefreshStartedEventArgs { AccountId = accountId, Source = source });

                    Interlocked.Increment(ref _activeRefreshCount);
                    try
                    {
                        result = await provider.QueryBalanceAsync(account, credentials, cancellationToken);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _activeRefreshCount);
                    }

                    record.LastQueryAttemptAt = NowUtc;
                    if (result.IsSuccess && result.Snapshot is { } snapshot)
                    {
                        record.LastSuccessfulSnapshot = snapshot;
                        record.LastQuerySuccessAt = record.LastQueryAttemptAt;
                        record.History.Add(new BalanceHistoryEntry
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            AccountId = accountId,
                            ProviderId = account.ProviderId,
                            SucceededAtUtc = snapshot.RetrievedAt,
                            Source = source,
                            IsAvailable = snapshot.IsAvailable,
                            Metrics = snapshot.Metrics,
                        });
                        record.History = HistoryRetention.Apply(record.History, NowUtc).ToList();
                    }
                    else if (!result.IsSuccess
                        && provider.Info.EffectiveCategory != ProviderCategory.ArtificialIntelligence)
                    {
                        // v0.9.0：地理/GIS 探测失败也写入历史（探测时间 + 状态 +
                        // 错误类别），供状态历史/成功率洞察；绝不保存响应内容。
                        var status = result.Error is { } error
                            ? MapErrorToStatus(error.Kind)
                            : GeospatialStatus.Unknown;
                        record.History.Add(new BalanceHistoryEntry
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            AccountId = accountId,
                            ProviderId = account.ProviderId,
                            SucceededAtUtc = NowUtc,
                            Source = source,
                            IsAvailable = false,
                            Metrics = new[]
                            {
                                GeospatialMetricFactory.ServiceAvailability(
                                    account.ProviderId,
                                    status),
                            },
                        });
                        record.History = HistoryRetention.Apply(record.History, NowUtc).ToList();
                    }
                }
            }

            // 手动/自动刷新完成后都重新计算下次自动刷新时间。
            account.Monitoring.NextRefreshAtUtc = account.Monitoring.AutoRefreshEnabled
                ? NowUtc.AddMinutes(account.Monitoring.RefreshIntervalMinutes)
                : null;

            _records[accountId] = record;
            await PersistAsync(cancellationToken);

            RefreshCompleted?.Invoke(
                this,
                new AccountRefreshCompletedEventArgs
                {
                    AccountId = accountId,
                    Result = result,
                    Source = source,
                });

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"刷新账户失败: {ex.GetType().Name}");
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                L10n.Get("Provider.ErrorUnexpected"));
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task RefreshAllAccountsAsync(
        BalanceQuerySource source,
        CancellationToken cancellationToken)
    {
        var accounts = _accounts.Where(a => a.HasCredential).ToList();
        foreach (var account in accounts)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _ = RefreshSafelyAsync(account.AccountId, source, cancellationToken);
            try
            {
                // 错峰：避免瞬间对同一 Provider 发起大量请求。
                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RefreshSafelyAsync(
        string accountId,
        BalanceQuerySource source,
        CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAccountAsync(accountId, source, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"刷新全部中账户失败: {ex.GetType().Name}");
        }
    }

    public Task<IReadOnlyList<string>> GetAutoRefreshDueAccountIdsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var due = _accounts
            .Where(a => a.Monitoring.AutoRefreshEnabled
                && (a.Monitoring.NextRefreshAtUtc is null || a.Monitoring.NextRefreshAtUtc <= nowUtc))
            .Select(a => a.AccountId)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(due);
    }

    public Task<IReadOnlyList<BalanceHistoryEntry>> GetHistoryAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(accountId, out var record))
        {
            return Task.FromResult<IReadOnlyList<BalanceHistoryEntry>>(
                new List<BalanceHistoryEntry>());
        }

        return Task.FromResult<IReadOnlyList<BalanceHistoryEntry>>(
            record.History
                .OrderByDescending(h => h.SucceededAtUtc)
                .ThenBy(h => h.Id)
                .ToList());
    }

    public async Task ClearHistoryAsync(string accountId, CancellationToken cancellationToken)
    {
        var semaphore = _refreshLocks.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_records.TryGetValue(accountId, out var record))
            {
                record.History.Clear();
            }

            await PersistAsync(cancellationToken);
            _log.Info($"已清除账户 {accountId} 的历史记录。");
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static DateTimeOffset? ComputeNextRefresh(
        ApiAccount? existing,
        MonitoringSettings monitoring,
        DateTimeOffset now)
    {
        if (!monitoring.AutoRefreshEnabled)
        {
            return null;
        }

        if (existing is null)
        {
            return now.AddMinutes(monitoring.RefreshIntervalMinutes);
        }

        var old = existing.Monitoring;
        if (!old.AutoRefreshEnabled
            || old.RefreshIntervalMinutes != monitoring.RefreshIntervalMinutes
            || old.NextRefreshAtUtc is null)
        {
            return now.AddMinutes(monitoring.RefreshIntervalMinutes);
        }

        return old.NextRefreshAtUtc;
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _accountStore.SaveAsync(_accounts, cancellationToken);
        await _snapshotStore.SaveAsync(_records.Values.ToList(), cancellationToken);
    }

    private static IReadOnlyDictionary<string, bool> BuildSlotPresence(
        IReadOnlyDictionary<string, bool> existing,
        IEnumerable<string> present)
    {
        var result = new Dictionary<string, bool>(existing, StringComparer.Ordinal);
        foreach (string slot in present)
        {
            result[slot] = true;
        }

        return result;
    }

    private static GeospatialStatus MapErrorToStatus(BalanceErrorKind kind) =>
        kind switch
        {
            BalanceErrorKind.Network => GeospatialStatus.NetworkUnavailable,
            BalanceErrorKind.Timeout => GeospatialStatus.Timeout,
            BalanceErrorKind.TlsFailure => GeospatialStatus.TlsFailure,
            BalanceErrorKind.CredentialInvalid or BalanceErrorKind.KeyTypeMismatch
                or BalanceErrorKind.SignatureInvalid or BalanceErrorKind.Unauthorized
                => GeospatialStatus.CredentialInvalid,
            BalanceErrorKind.PermissionDenied or BalanceErrorKind.IpWhitelistDenied
                or BalanceErrorKind.RefererDomainDenied or BalanceErrorKind.Forbidden
                => GeospatialStatus.PermissionDenied,
            BalanceErrorKind.ServiceNotEnabled => GeospatialStatus.ServiceNotEnabled,
            BalanceErrorKind.QuotaExceeded or BalanceErrorKind.PaymentRequired
                => GeospatialStatus.QuotaExceeded,
            BalanceErrorKind.RateLimited => GeospatialStatus.RateLimited,
            BalanceErrorKind.ConfigurationMissing or BalanceErrorKind.MissingCredential
                => GeospatialStatus.ConfigurationMissing,
            _ => GeospatialStatus.ProviderError,
        };
}
