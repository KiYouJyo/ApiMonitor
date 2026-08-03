using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.Tests.TestHelpers;
using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 紧凑窗口 ViewModel 测试：账户/币种选择、数据同步、刷新状态与阈值显示。
/// 不弹出真实窗口。
/// </summary>
public sealed class CompactWindowViewModelTests
{
    private sealed class Harness : IDisposable
    {
        public FakeAccountManager Manager { get; } = new();

        public TempDirectory Temp { get; } = new();

        public CompactWindowViewModel ViewModel { get; }

        public Harness()
        {
            ViewModel = new CompactWindowViewModel(
                Manager,
                new CompactWindowSettingsStore(Temp.Path),
                new AppLog(Temp.Path),
                new FakeUiThreadInvoker());
        }

        public void Dispose() => Temp.Dispose();
    }

    private static ApiAccount Account(string id, string name, MonitoringSettings? monitoring = null) =>
        new()
        {
            AccountId = id,
            ProviderId = "deepseek",
            DisplayName = name,
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Monitoring = monitoring ?? new MonitoringSettings(),
        };

    private static AccountBalanceRecord Record(string accountId, params (string Currency, decimal Total)[] balances)
    {
        var snapshot = new BalanceSnapshot
        {
            AccountId = accountId,
            ProviderId = "deepseek",
            IsAvailable = true,
            RetrievedAt = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
            Balances = balances
                .Select(b => new BalanceAmount
                {
                    Currency = b.Currency,
                    TotalBalance = b.Total,
                    GrantedBalance = 0,
                    ToppedUpBalance = 0,
                })
                .ToList(),
        };

        return new AccountBalanceRecord
        {
            AccountId = accountId,
            ProviderId = "deepseek",
            LastQueryAttemptAt = snapshot.RetrievedAt,
            LastQuerySuccessAt = snapshot.RetrievedAt,
            LastSuccessfulSnapshot = snapshot,
        };
    }

    private static BalanceQueryResult SuccessResult(string accountId, params (string Currency, decimal Total)[] balances) =>
        BalanceQueryResult.Success(new BalanceSnapshot
        {
            AccountId = accountId,
            ProviderId = "deepseek",
            IsAvailable = true,
            RetrievedAt = DateTimeOffset.UtcNow,
            Balances = balances
                .Select(b => new BalanceAmount
                {
                    Currency = b.Currency,
                    TotalBalance = b.Total,
                    GrantedBalance = 0,
                    ToppedUpBalance = 0,
                })
                .ToList(),
        });

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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!await condition())
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

        Assert.False(h.ViewModel.HasAccounts);
        Assert.Equal("尚未添加 API 账户", h.ViewModel.StatusText);
        Assert.Equal("—", h.ViewModel.BalanceText);
    }

    [Fact]
    public async Task Initialize_NoSavedSelection_SelectsFirstAccountAndCurrency()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Accounts.Add(Account("acct-b", "B账户"));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 123.45m), ("USD", 10m));

        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.Equal("acct-a", h.ViewModel.SelectedAccount?.AccountId);
        Assert.Equal("CNY", h.ViewModel.SelectedCurrency);
        Assert.Equal("123.45", h.ViewModel.BalanceText);
        Assert.Equal("未知", h.ViewModel.StatusText);
        Assert.True(h.ViewModel.HasAccounts);
        Assert.True(h.ViewModel.HasSnapshot);
    }

    [Fact]
    public async Task Initialize_SavedAccountSelection_IsRestored()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Accounts.Add(Account("acct-b", "B账户"));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 1m));
        h.Manager.Records["acct-b"] = Record("acct-b", ("USD", 2m));
        var store = new CompactWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(new CompactWindowSettings
        {
            SelectedAccountId = "acct-b",
            SelectedCurrency = "USD",
            IsAlwaysOnTop = true,
        }, CancellationToken.None);

        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.Equal("acct-b", h.ViewModel.SelectedAccount?.AccountId);
        Assert.Equal("USD", h.ViewModel.SelectedCurrency);
    }

    [Fact]
    public async Task DeletedCurrentAccount_FallsBackToFirstAccount()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Accounts.Add(Account("acct-b", "B账户"));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 1m));
        h.Manager.Records["acct-b"] = Record("acct-b", ("USD", 2m));
        var store = new CompactWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(new CompactWindowSettings
        {
            SelectedAccountId = "acct-a",
        }, CancellationToken.None);

        await h.ViewModel.InitializeAsync(CancellationToken.None);
        Assert.Equal("acct-a", h.ViewModel.SelectedAccount?.AccountId);

        // 模拟删除当前账户后仅剩 acct-b。
        h.Manager.Accounts.RemoveAll(a => a.AccountId == "acct-a");
        h.Manager.Records.Remove("acct-a");
        h.Manager.RaiseAccountsChanged();

        await WaitUntilAsync(() =>
            h.ViewModel.SelectedAccount?.AccountId == "acct-b"
            && h.ViewModel.SelectedCurrency == "USD");
        Assert.Equal("acct-b", h.ViewModel.SelectedAccount?.AccountId);
        Assert.Equal("USD", h.ViewModel.SelectedCurrency);
    }

    [Fact]
    public async Task MissingCurrency_FallsBackToFirstAvailable()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 1m));
        var store = new CompactWindowSettingsStore(h.Temp.Path);
        await store.SaveAsync(new CompactWindowSettings
        {
            SelectedAccountId = "acct-a",
            SelectedCurrency = "USD",
        }, CancellationToken.None);

        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.Equal("CNY", h.ViewModel.SelectedCurrency);
    }

    [Fact]
    public async Task ManualRefresh_Success_UpdatesBalance()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 1m));
        await h.ViewModel.InitializeAsync(CancellationToken.None);
        h.Manager.RefreshResult = SuccessResult("acct-a", ("CNY", 88.88m));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 88.88m));

        await h.ViewModel.RefreshAsync();

        Assert.Equal("88.88", h.ViewModel.BalanceText);
        Assert.Equal(1, h.Manager.RefreshCalls);
        Assert.False(h.ViewModel.HasError);
    }

    [Fact]
    public async Task ManualRefresh_Failure_KeepsOldBalanceAndShowsError()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 55m));
        await h.ViewModel.InitializeAsync(CancellationToken.None);
        h.Manager.RefreshResult = BalanceQueryResult.Failure(BalanceErrorKind.Network, "网络错误");

        await h.ViewModel.RefreshAsync();

        Assert.Equal("55.00", h.ViewModel.BalanceText);
        Assert.True(h.ViewModel.HasError);
        Assert.Equal("网络错误", h.ViewModel.ErrorText);
    }

    [Fact]
    public async Task AutoRefreshCompleted_SyncsCompactWindow()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 1m));
        await h.ViewModel.InitializeAsync(CancellationToken.None);

        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 66.6m));
        h.Manager.RaiseRefreshCompleted(
            "acct-a",
            SuccessResult("acct-a", ("CNY", 66.6m)),
            BalanceQuerySource.Automatic);

        await WaitUntilAsync(() => h.ViewModel.BalanceText == "66.60");
        Assert.Equal("66.60", h.ViewModel.BalanceText);
    }

    [Fact]
    public async Task AccountsChanged_AfterRename_UpdatesOption()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "旧名称"));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 1m));
        await h.ViewModel.InitializeAsync(CancellationToken.None);

        h.Manager.Accounts[0] = Account("acct-a", "新名称");
        h.Manager.RaiseAccountsChanged();

        await WaitUntilAsync(() => h.ViewModel.SelectedAccount?.DisplayName == "新名称");
        Assert.Equal("新名称", h.ViewModel.SelectedAccount?.DisplayName);
    }

    [Fact]
    public async Task ThresholdChange_UpdatesStatusImmediately()
    {
        using var h = new Harness();
        var monitoring = new MonitoringSettings
        {
            Thresholds = new List<BalanceThresholdRule>
            {
                new()
                {
                    Currency = "CNY",
                    IsEnabled = true,
                    ThresholdAmount = 100m,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                },
            },
        };
        h.Manager.Accounts.Add(Account("acct-a", "A账户", monitoring));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 50m));
        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.Equal("低余额", h.ViewModel.StatusText);

        // 阈值提高后状态恢复为正常。
        h.Manager.Accounts[0] = Account(
            "acct-a",
            "A账户",
            new MonitoringSettings
            {
                Thresholds = new List<BalanceThresholdRule>
                {
                    new()
                    {
                        Currency = "CNY",
                        IsEnabled = true,
                        ThresholdAmount = 10m,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    },
                },
            });
        h.Manager.RaiseAccountsChanged();

        await WaitUntilAsync(() => h.ViewModel.StatusText == "正常");
        Assert.Equal("正常", h.ViewModel.StatusText);
    }

    [Fact]
    public async Task AlwaysOnTop_DefaultOn_AndPersistedAfterToggle()
    {
        using var h = new Harness();
        await h.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.True(h.ViewModel.IsAlwaysOnTop);

        h.ViewModel.IsAlwaysOnTop = false;

        var store = new CompactWindowSettingsStore(h.Temp.Path);
        await WaitUntilAsync(async () =>
        {
            var settings = await store.LoadAsync(CancellationToken.None);
            return !settings.IsAlwaysOnTop;
        });
    }

    [Fact]
    public async Task BusyRefresh_DoesNotStartSecondQuery()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 1m));
        await h.ViewModel.InitializeAsync(CancellationToken.None);

        var gate = new TaskCompletionSource();
        h.Manager.RefreshGate = gate;
        var first = h.ViewModel.RefreshAsync();
        await Task.Yield();

        await h.ViewModel.RefreshAsync();
        gate.SetResult();
        await first;

        Assert.Equal(1, h.Manager.RefreshCalls);
    }

    [Fact]
    public async Task RefreshWithNewCurrency_UpdatesCurrencyList()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 1m));
        await h.ViewModel.InitializeAsync(CancellationToken.None);

        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 1m), ("EUR", 2m));
        h.Manager.RaiseRefreshCompleted(
            "acct-a",
            SuccessResult("acct-a", ("CNY", 1m), ("EUR", 2m)),
            BalanceQuerySource.Automatic);

        await WaitUntilAsync(() => h.ViewModel.CurrencyOptions.Contains("EUR"));
        Assert.Contains("EUR", h.ViewModel.CurrencyOptions);
    }

    [Fact]
    public async Task Shutdown_UnsubscribesAccountManagerEvents()
    {
        using var h = new Harness();
        h.Manager.Accounts.Add(Account("acct-a", "A账户"));
        h.Manager.Records["acct-a"] = Record("acct-a", ("CNY", 1m));
        await h.ViewModel.InitializeAsync(CancellationToken.None);

        h.ViewModel.Shutdown();
        h.Manager.RaiseRefreshCompleted(
            "acct-a",
            SuccessResult("acct-a", ("CNY", 99m)),
            BalanceQuerySource.Automatic);
        h.Manager.RaiseAccountsChanged();

        Assert.Equal("1.00", h.ViewModel.BalanceText);
    }
}
