using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.Tests.TestHelpers;
using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 悬浮余额窗 ViewModel 测试（v0.7.0）：选定账户展示、账户切换、
/// 删除安全回退、失败状态、未知额度、主额度选择与持久化。不弹出真实窗口。
/// </summary>
public sealed class FloatingWindowViewModelTests
{
    private sealed class Harness : IDisposable
    {
        public FakeAccountManager Manager { get; } = new();

        public TempDirectory Temp { get; } = new();

        public FloatingWindowViewModel ViewModel { get; }

        public Harness()
        {
            ViewModel = new FloatingWindowViewModel(
                Manager,
                new FloatingWindowSettingsStore(Temp.Path),
                new AppLog(Temp.Path),
                new FakeUiThreadInvoker());
        }

        public void Dispose() => Temp.Dispose();
    }

    private static ApiAccount Account(string id, string name, string providerId = "deepseek") =>
        new()
        {
            AccountId = id,
            ProviderId = providerId,
            DisplayName = name,
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Monitoring = new MonitoringSettings(),
        };

    private static BalanceMetric Metric(
        string metricId,
        string displayName,
        string unit,
        BalanceMetricKind kind,
        decimal? available = null,
        decimal? total = null,
        decimal? used = null,
        bool thresholdSupported = false) =>
        new()
        {
            MetricId = metricId,
            DisplayName = displayName,
            Unit = unit,
            Kind = kind,
            AvailableAmount = available,
            TotalAmount = total,
            UsedAmount = used,
            IsThresholdSupported = thresholdSupported,
        };

    private static AccountBalanceRecord Record(
        string accountId,
        string providerId,
        IReadOnlyList<BalanceMetric> metrics,
        DateTimeOffset? retrievedAt = null,
        DateTimeOffset? failedAttemptAt = null,
        DateTimeOffset? successAt = null)
    {
        var snapshot = new BalanceSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            AccountId = accountId,
            ProviderId = providerId,
            IsAvailable = true,
            RetrievedAt = retrievedAt ?? new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
            Metrics = metrics,
        };

        return new AccountBalanceRecord
        {
            AccountId = accountId,
            ProviderId = providerId,
            LastQueryAttemptAt = failedAttemptAt ?? snapshot.RetrievedAt,
            LastQuerySuccessAt = successAt ?? snapshot.RetrievedAt,
            LastSuccessfulSnapshot = snapshot,
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("条件在超时时间内未满足。");
            }

            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Initialize_NoAccounts_ShowsEmptyState()
    {
        using var h = new Harness();

        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.False(h.ViewModel.HasAccount);
        Assert.Equal("尚未添加 API 账户", h.ViewModel.EmptyText);
        Assert.Equal("—", h.ViewModel.BalanceText);
    }

    [Fact]
    public async Task Initialize_NoSavedSelection_ShowsNoSelectedAccount()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Accounts.Add(Account("acct-b", "B账户"));

        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.False(h.ViewModel.HasAccount);
        Assert.Equal("未选择账户", h.ViewModel.EmptyText);
    }

    [Fact]
    public async Task Initialize_SavedSelection_ShowsDeepSeekBalance()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Records["acct-a"] = Record(
            "acct-a",
            "deepseek",
            new[]
            {
                Metric("deepseek:CNY:total", "CNY 总余额", "CNY", BalanceMetricKind.MonetaryBalance, available: 123.45m, total: 123.45m),
            });
        var store = new FloatingWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(
            new FloatingWindowSettings { SelectedAccountId = "acct-a" },
            CancellationToken.None);

        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.True(h.ViewModel.HasAccount);
        Assert.Equal("A账户", h.ViewModel.AccountName);
        Assert.Equal("DeepSeek", h.ViewModel.ProviderName);
        Assert.Equal("123.45", h.ViewModel.BalanceText);
        Assert.Equal("CNY", h.ViewModel.UnitText);
        Assert.Contains("最近更新：", h.ViewModel.LastUpdatedText);
    }

    [Fact]
    public async Task Initialize_LowBalance_UsesShortLowStatus()
    {
        using var h = new Harness();
        var account = Account("acct-low", "低余额账户");
        account.Monitoring.Thresholds.Add(new BalanceThresholdRule
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            IsEnabled = true,
            ThresholdAmount = 20m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        h.Manager.Accounts.Add(account);
        h.Manager.Records["acct-low"] = Record(
            "acct-low",
            "deepseek",
            new[] { Metric("deepseek:CNY:total", "CNY 总余额", "CNY", BalanceMetricKind.MonetaryBalance, available: 10m, total: 10m, thresholdSupported: true) });
        var store = new FloatingWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(new FloatingWindowSettings { SelectedAccountId = "acct-low" }, CancellationToken.None);

        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.Equal("低余额", h.ViewModel.StatusText);
    }

    [Fact]
    public async Task ShowAccountAsync_SwitchesFromAToB()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Accounts.Add(Account("acct-b", "B账户"));
        h.Manager.Records["acct-a"] = Record(
            "acct-a",
            "deepseek",
            new[] { Metric("deepseek:CNY:total", "CNY 总余额", "CNY", BalanceMetricKind.MonetaryBalance, available: 10m, total: 10m) });
        h.Manager.Records["acct-b"] = Record(
            "acct-b",
            "deepseek",
            new[] { Metric("deepseek:USD:total", "USD 总余额", "USD", BalanceMetricKind.MonetaryBalance, available: 20m, total: 20m) });

        await h.ViewModel.ShowAccountAsync("acct-a", CancellationToken.None);
        Assert.Equal("A账户", h.ViewModel.AccountName);
        Assert.Equal("10.00", h.ViewModel.BalanceText);

        await h.ViewModel.ShowAccountAsync("acct-b", CancellationToken.None);

        Assert.Equal("B账户", h.ViewModel.AccountName);
        Assert.Equal("20.00", h.ViewModel.BalanceText);
        Assert.Equal("USD", h.ViewModel.UnitText);
    }

    [Fact]
    public async Task ShowAccountAsync_PersistsSelectedAccount()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Records["acct-a"] = Record(
            "acct-a",
            "deepseek",
            new[] { Metric("deepseek:CNY:total", "CNY 总余额", "CNY", BalanceMetricKind.MonetaryBalance, available: 1m, total: 1m) });

        await h.ViewModel.ShowAccountAsync("acct-a", CancellationToken.None);

        var store = new FloatingWindowSettingsStore(h.Temp.Path);
        var settings = await store.LoadAsync(CancellationToken.None);
        Assert.Equal("acct-a", settings.SelectedAccountId);
    }

    [Fact]
    public async Task DeletedCurrentAccount_SafeEmptyState()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Accounts.Add(Account("acct-b", "B账户"));
        h.Manager.Records["acct-a"] = Record(
            "acct-a",
            "deepseek",
            new[] { Metric("deepseek:CNY:total", "CNY 总余额", "CNY", BalanceMetricKind.MonetaryBalance, available: 1m, total: 1m) });
        var store = new FloatingWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(
            new FloatingWindowSettings { SelectedAccountId = "acct-a" },
            CancellationToken.None);
        await h.ViewModel.InitializeAsync(CancellationToken.None);
        Assert.True(h.ViewModel.HasAccount);

        h.Manager.Accounts.RemoveAll(a => a.AccountId == "acct-a");
        h.Manager.Records.Remove("acct-a");
        h.Manager.RaiseAccountsChanged();

        await WaitUntilAsync(() => !h.ViewModel.HasAccount);
        Assert.False(h.ViewModel.HasAccount);
        Assert.Equal("未选择账户", h.ViewModel.EmptyText);
        Assert.Equal("—", h.ViewModel.BalanceText);
    }

    [Fact]
    public async Task QueryFailed_ShowsFailureInsteadOfOldNumber()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        // 最近一次查询失败：attempt 晚于上次成功。
        h.Manager.Records["acct-a"] = Record(
            "acct-a",
            "deepseek",
            new[] { Metric("deepseek:CNY:total", "CNY 总余额", "CNY", BalanceMetricKind.MonetaryBalance, available: 55m, total: 55m) },
            failedAttemptAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            successAt: new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero));
        var store = new FloatingWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(
            new FloatingWindowSettings { SelectedAccountId = "acct-a" },
            CancellationToken.None);

        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.Equal("—", h.ViewModel.BalanceText);
        Assert.Equal("失败", h.ViewModel.StatusText);
    }

    [Fact]
    public async Task NoMainAmount_ShowsUnknown()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        // 指标全部数值缺失：无可显示主额度。
        h.Manager.Records["acct-a"] = Record(
            "acct-a",
            "deepseek",
            new[] { Metric("deepseek:CNY:total", "CNY 总余额", "CNY", BalanceMetricKind.MonetaryBalance) });
        var store = new FloatingWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(
            new FloatingWindowSettings { SelectedAccountId = "acct-a" },
            CancellationToken.None);

        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.Equal("未知", h.ViewModel.BalanceText);
    }

    [Fact]
    public async Task OpenRouterApiKey_ShowsRemainingQuota()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-or", "OR 账户", "openrouter"));
        h.Manager.Records["acct-or"] = Record(
            "acct-or",
            "openrouter",
            new[]
            {
                Metric("openrouter:key:quota-remaining", "密钥剩余额度", "credits", BalanceMetricKind.KeyQuota, available: 42m, total: 100m),
                Metric("openrouter:key:usage-total", "累计使用量", "credits", BalanceMetricKind.Usage, used: 58m),
            });
        var store = new FloatingWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(
            new FloatingWindowSettings { SelectedAccountId = "acct-or" },
            CancellationToken.None);

        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.Equal("42.00", h.ViewModel.BalanceText);
        Assert.Equal("credits", h.ViewModel.UnitText);
    }

    [Fact]
    public async Task OpenRouterManagementKey_ShowsRemainingCredits()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-mgmt", "OR 管理账户", "openrouter"));
        h.Manager.Records["acct-mgmt"] = Record(
            "acct-mgmt",
            "openrouter",
            new[]
            {
                Metric("openrouter:credits:remaining", "剩余 Credits", "credits", BalanceMetricKind.PlatformCredits, available: 8.5m, total: 100m, used: 91.5m),
                Metric("openrouter:credits:usage", "累计使用 Credits", "credits", BalanceMetricKind.Usage, used: 91.5m),
            });
        var store = new FloatingWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(
            new FloatingWindowSettings { SelectedAccountId = "acct-mgmt" },
            CancellationToken.None);

        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.Equal("8.50", h.ViewModel.BalanceText);
        Assert.Equal("credits", h.ViewModel.UnitText);
    }

    [Fact]
    public async Task AutoRefreshCompleted_SyncsDisplay()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Records["acct-a"] = Record(
            "acct-a",
            "deepseek",
            new[] { Metric("deepseek:CNY:total", "CNY 总余额", "CNY", BalanceMetricKind.MonetaryBalance, available: 1m, total: 1m) });
        var store = new FloatingWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(
            new FloatingWindowSettings { SelectedAccountId = "acct-a" },
            CancellationToken.None);
        await h.ViewModel.InitializeAsync(CancellationToken.None);
        Assert.Equal("1.00", h.ViewModel.BalanceText);

        h.Manager.Records["acct-a"] = Record(
            "acct-a",
            "deepseek",
            new[] { Metric("deepseek:CNY:total", "CNY 总余额", "CNY", BalanceMetricKind.MonetaryBalance, available: 66.6m, total: 66.6m) });
        h.Manager.RaiseRefreshCompleted(
            "acct-a",
            BalanceQueryResult.Success(h.Manager.Records["acct-a"]!.LastSuccessfulSnapshot!),
            BalanceQuerySource.Automatic);

        await WaitUntilAsync(() => h.ViewModel.BalanceText == "66.60");
        Assert.Equal("66.60", h.ViewModel.BalanceText);
    }

    [Fact]
    public async Task Shutdown_UnsubscribesAccountManagerEvents()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Records["acct-a"] = Record(
            "acct-a",
            "deepseek",
            new[] { Metric("deepseek:CNY:total", "CNY 总余额", "CNY", BalanceMetricKind.MonetaryBalance, available: 1m, total: 1m) });
        var store = new FloatingWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(
            new FloatingWindowSettings { SelectedAccountId = "acct-a" },
            CancellationToken.None);
        await h.ViewModel.InitializeAsync(CancellationToken.None);

        h.ViewModel.Shutdown();
        h.Manager.Records["acct-a"] = Record(
            "acct-a",
            "deepseek",
            new[] { Metric("deepseek:CNY:total", "CNY 总余额", "CNY", BalanceMetricKind.MonetaryBalance, available: 99m, total: 99m) });
        h.Manager.RaiseRefreshCompleted(
            "acct-a",
            BalanceQueryResult.Success(h.Manager.Records["acct-a"]!.LastSuccessfulSnapshot!),
            BalanceQuerySource.Automatic);
        h.Manager.RaiseAccountsChanged();

        Assert.Equal("1.00", h.ViewModel.BalanceText);
    }
}
