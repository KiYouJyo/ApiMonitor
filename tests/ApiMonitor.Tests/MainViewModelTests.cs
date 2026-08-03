using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.ViewModels;
using System.Reflection;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void SubtitleText_MatchesAssemblyVersion()
    {
        var (vm, _, _, _, _) = CreateSut();
        var version = Assembly.GetExecutingAssembly().GetName().Version;

        Assert.NotNull(version);
        Assert.Contains($"v{version!.Major}.{version.Minor}.{version.Build}", vm.SubtitleText);
        Assert.DoesNotContain("v0.1.0", vm.SubtitleText);
    }

    private static ApiAccount Account(string id = "acct-1") =>
        new()
        {
            AccountId = id,
            ProviderId = "deepseek",
            DisplayName = "测试账户",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static BalanceSnapshot Snapshot() =>
        new()
        {
            SnapshotId = "snap-main",
            AccountId = "acct-1",
            ProviderId = "deepseek",
            IsAvailable = true,
            RetrievedAt = DateTimeOffset.UtcNow,
            Metrics = new[]
            {
                new BalanceMetric
                {
                    MetricId = "deepseek:CNY:total",
                    DisplayName = "CNY 总余额",
                    Unit = "CNY",
                    Kind = BalanceMetricKind.MonetaryBalance,
                    AvailableAmount = 42.00m,
                    TotalAmount = 42.00m,
                    GrantedAmount = 2.00m,
                    ToppedUpAmount = 40.00m,
                    IsThresholdSupported = true,
                },
            },
        };

    private static (
        MainViewModel ViewModel,
        FakeAccountManager Manager,
        FakeDialogService Dialogs,
        FakeClipboardService Clipboard,
        FakeUiThreadInvoker Ui) CreateSut()
    {
        var manager = new FakeAccountManager();
        var dialogs = new FakeDialogService();
        var clipboard = new FakeClipboardService();
        var ui = new FakeUiThreadInvoker();
        var log = new AppLog(Path.Combine(Path.GetTempPath(), $"abm-log-{Guid.NewGuid():N}"));
        var viewModel = new MainViewModel(manager, dialogs, log, clipboard, ui);
        return (viewModel, manager, dialogs, clipboard, ui);
    }

    [Fact]
    public async Task InitializeAsync_PopulatesAccounts()
    {
        var (viewModel, manager, _, _, _) = CreateSut();
        manager.Accounts.Add(Account());

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.HasAccounts);
        Assert.Single(viewModel.Accounts);
        Assert.Equal("测试账户", viewModel.Accounts[0].DisplayName);
    }

    [Fact]
    public async Task InitializeAsync_ShowsRecoveryWarning()
    {
        var (viewModel, manager, _, _, _) = CreateSut();
        manager.RecoveryMessagesList.Add("accounts.json 内容损坏，已备份并重置。");

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsStatusVisible);
        Assert.Equal(StatusSeverity.Warning, viewModel.StatusSeverity);
        Assert.Contains("恢复", viewModel.StatusTitle);
    }

    [Fact]
    public async Task RefreshSuccess_UpdatesItemAndShowsSuccess()
    {
        var (viewModel, manager, _, _, _) = CreateSut();
        manager.Accounts.Add(Account());
        manager.RefreshResult = BalanceQueryResult.Success(Snapshot());
        await viewModel.InitializeAsync();
        var item = viewModel.Accounts[0];

        await viewModel.RefreshAccountAsync(item.Account.AccountId);

        Assert.False(item.IsRefreshing);
        Assert.True(item.IsAvailable);
        Assert.True(item.HasSnapshot);
        Assert.Equal("CNY · 总额 42.00 · 赠送 2.00 · 充值 40.00", item.BalanceLines[0].LineText);
        Assert.Equal(StatusSeverity.Success, viewModel.StatusSeverity);
        Assert.True(viewModel.IsStatusVisible);
    }

    [Fact]
    public async Task RefreshFailure_ShowsErrorAndKeepsNoSnapshot()
    {
        var (viewModel, manager, _, _, _) = CreateSut();
        manager.Accounts.Add(Account());
        manager.RefreshResult = BalanceQueryResult.Failure(
            BalanceErrorKind.Unauthorized,
            "API Key 无效或已过期（401）。");
        await viewModel.InitializeAsync();
        var item = viewModel.Accounts[0];

        await viewModel.RefreshAccountAsync(item.Account.AccountId);

        Assert.False(item.IsRefreshing);
        Assert.False(item.HasSnapshot);
        Assert.False(item.IsAvailable);
        Assert.True(item.HasLastError);
        Assert.Contains("401", item.LastErrorText);
        Assert.Equal(StatusSeverity.Error, viewModel.StatusSeverity);
        Assert.Equal("查询失败", viewModel.StatusTitle);
    }

    [Fact]
    public async Task RefreshState_IsDrivenByEvents_AndPreventsDuplicateClicks()
    {
        var (viewModel, manager, _, _, _) = CreateSut();
        manager.Accounts.Add(Account());
        manager.RefreshResult = BalanceQueryResult.Success(Snapshot());
        await viewModel.InitializeAsync();
        var item = viewModel.Accounts[0];

        manager.RaiseRefreshStarted("acct-1", BalanceQuerySource.Manual);
        Assert.True(item.IsRefreshing);
        Assert.False(item.RefreshCommand.CanExecute(null));

        manager.RaiseRefreshCompleted("acct-1", manager.RefreshResult, BalanceQuerySource.Manual);

        Assert.False(item.IsRefreshing);
        Assert.True(item.RefreshCommand.CanExecute(null));
        Assert.True(item.HasSnapshot);
    }

    [Fact]
    public async Task InitializeShowsLoadingState()
    {
        var (viewModel, manager, _, _, _) = CreateSut();
        manager.Accounts.Add(Account());
        manager.LoadGate = new TaskCompletionSource();

        var initialize = viewModel.InitializeAsync();
        await Task.Delay(50);

        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.AddAccountCommand.CanExecute(null));

        manager.LoadGate.SetResult();
        await initialize;

        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.AddAccountCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddAccount_SavesViaDialogResult()
    {
        var (viewModel, manager, dialogs, _, _) = CreateSut();
        dialogs.EditorResult = new AccountEditorResult
        {
            SaveRequested = true,
            ProviderId = "deepseek",
            DisplayName = "新账户",
            ApiKey = "sk-new",
        };

        await viewModel.AddAccountAsync();

        Assert.Equal(1, manager.SaveCalls);
        Assert.True(viewModel.HasAccounts);
        Assert.Equal(StatusSeverity.Success, viewModel.StatusSeverity);
    }

    [Fact]
    public async Task DeleteAccount_AfterConfirmation_Deletes()
    {
        var (viewModel, manager, dialogs, _, _) = CreateSut();
        manager.Accounts.Add(Account());
        dialogs.ConfirmDeleteResult = true;
        await viewModel.InitializeAsync();

        await viewModel.DeleteAccountAsync("acct-1");

        Assert.Equal(1, manager.DeleteCalls);
        Assert.False(viewModel.HasAccounts);
        Assert.Equal(StatusSeverity.Success, viewModel.StatusSeverity);
    }

    [Fact]
    public async Task DeleteAccount_Cancel_DoesNotDelete()
    {
        var (viewModel, manager, dialogs, _, _) = CreateSut();
        manager.Accounts.Add(Account());
        dialogs.ConfirmDeleteResult = false;
        await viewModel.InitializeAsync();

        await viewModel.DeleteAccountAsync("acct-1");

        Assert.Equal(0, manager.DeleteCalls);
        Assert.True(viewModel.HasAccounts);
    }

    [Fact]
    public async Task CopyKey_FoundCredential_CallsClipboardWithKeyAndShowsSuccess()
    {
        var (viewModel, manager, _, clipboard, _) = CreateSut();
        manager.Accounts.Add(Account());
        manager.ApiKeyResult = "sk-test-only-not-real";
        await viewModel.InitializeAsync();

        await viewModel.CopyKeyAsync("acct-1");

        var copied = Assert.Single(clipboard.SetCalls);
        Assert.Equal("sk-test-only-not-real", copied);
        Assert.Equal(TimeSpan.FromSeconds(30), clipboard.LastClearAfter);
        Assert.Equal(StatusSeverity.Success, viewModel.StatusSeverity);
        Assert.True(viewModel.IsStatusVisible);
        Assert.Contains("已复制", viewModel.StatusTitle);
    }

    [Fact]
    public async Task CopyKey_MissingCredential_DoesNotTouchClipboardAndShowsError()
    {
        var (viewModel, manager, _, clipboard, _) = CreateSut();
        manager.Accounts.Add(Account());
        manager.ApiKeyResult = null;
        await viewModel.InitializeAsync();

        await viewModel.CopyKeyAsync("acct-1");

        Assert.Empty(clipboard.SetCalls);
        Assert.Equal(StatusSeverity.Error, viewModel.StatusSeverity);
        Assert.Contains("未找到该账户保存的 API Key", viewModel.StatusMessage);
    }

    [Fact]
    public async Task CopyKey_ResetsBusyStateAndDoesNotLogKey()
    {
        var manager = new FakeAccountManager();
        manager.Accounts.Add(Account());
        manager.ApiKeyResult = "sk-test-only-not-real";
        var dialogs = new FakeDialogService();
        var clipboard = new FakeClipboardService();
        string logDir = Path.Combine(Path.GetTempPath(), $"abm-log-{Guid.NewGuid():N}");
        var log = new AppLog(logDir);
        var viewModel = new MainViewModel(manager, dialogs, log, clipboard, new FakeUiThreadInvoker());
        await viewModel.InitializeAsync();
        var item = viewModel.Accounts[0];

        await viewModel.CopyKeyAsync("acct-1");

        Assert.False(item.IsCopying);
        Assert.True(item.CopyKeyCommand.CanExecute(null));

        string logPath = Path.Combine(logDir, "app.log");
        if (File.Exists(logPath))
        {
            string logContent = File.ReadAllText(logPath);
            Assert.DoesNotContain("sk-test-only-not-real", logContent, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AutoRefreshEvents_UpdateCardStateAndShowStatus()
    {
        var (viewModel, manager, _, _, _) = CreateSut();
        manager.Accounts.Add(Account());
        await viewModel.InitializeAsync();
        var item = viewModel.Accounts[0];

        manager.RaiseRefreshStarted("acct-1", BalanceQuerySource.Automatic);
        Assert.True(item.IsRefreshing);

        manager.RefreshResult = BalanceQueryResult.Success(Snapshot());
        manager.RaiseRefreshCompleted("acct-1", manager.RefreshResult, BalanceQuerySource.Automatic);

        Assert.False(item.IsRefreshing);
        Assert.True(item.HasSnapshot);
        Assert.Equal(StatusSeverity.Success, viewModel.StatusSeverity);
        Assert.Equal("自动刷新完成", viewModel.StatusTitle);
    }

    [Fact]
    public async Task AutoRefreshFailure_KeepsSnapshotAndShowsWarning()
    {
        var (viewModel, manager, _, _, _) = CreateSut();
        manager.Accounts.Add(Account());
        await viewModel.InitializeAsync();
        var item = viewModel.Accounts[0];

        manager.RefreshResult = BalanceQueryResult.Success(Snapshot());
        manager.RaiseRefreshCompleted("acct-1", manager.RefreshResult, BalanceQuerySource.Automatic);
        string lastSuccessBefore = item.LastSuccessLine;

        manager.RefreshResult = BalanceQueryResult.Failure(
            BalanceErrorKind.Network,
            "无法连接网络。");
        manager.RaiseRefreshCompleted("acct-1", manager.RefreshResult, BalanceQuerySource.Automatic);

        Assert.True(item.HasSnapshot);
        Assert.Equal(lastSuccessBefore, item.LastSuccessLine);
        Assert.Equal(StatusSeverity.Warning, viewModel.StatusSeverity);
        Assert.Equal("自动刷新失败", viewModel.StatusTitle);
    }

    [Fact]
    public async Task Filters_ByProviderAndStatus_UpdateVisibleAccounts()
    {
        var (viewModel, manager, _, _, _) = CreateSut();
        manager.Accounts.Add(Account("acct-deepseek"));
        manager.Accounts.Add(new ApiAccount
        {
            AccountId = "acct-or",
            ProviderId = "openrouter",
            DisplayName = "OR 账户",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await viewModel.InitializeAsync();

        Assert.Equal(2, viewModel.FilteredAccounts.Count);

        viewModel.SelectedProviderFilter = "deepseek";
        var visible = Assert.Single(viewModel.FilteredAccounts);
        Assert.Equal("acct-deepseek", visible.Account.AccountId);

        viewModel.SelectedProviderFilter = "openrouter";
        Assert.Equal("acct-or", Assert.Single(viewModel.FilteredAccounts).Account.AccountId);

        viewModel.SelectedStatusFilter = AccountStatusFilter.Normal;
        Assert.True(viewModel.HasActiveFilters);

        viewModel.SelectedStatusFilter = AccountStatusFilter.Unknown;
        Assert.Equal("acct-or", Assert.Single(viewModel.FilteredAccounts).Account.AccountId);
    }

    [Fact]
    public async Task SummaryCounts_ReflectLowAndFailedAccounts()
    {
        var (viewModel, manager, _, _, _) = CreateSut();
        var low = Account("acct-low");
        low.Monitoring.Thresholds.Add(new BalanceThresholdRule
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            IsEnabled = true,
            ThresholdAmount = 100m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        manager.Accounts.Add(low);
        manager.Accounts.Add(Account("acct-fail"));
        manager.Records["acct-low"] = new AccountBalanceRecord
        {
            AccountId = "acct-low",
            ProviderId = "deepseek",
            LastSuccessfulSnapshot = TestHelpers.TestMetrics.Snapshot(
                "acct-low",
                DateTimeOffset.UtcNow,
                TestHelpers.TestMetrics.Cny(10m)),
        };
        manager.Records["acct-fail"] = new AccountBalanceRecord
        {
            AccountId = "acct-fail",
            ProviderId = "deepseek",
            LastQueryAttemptAt = DateTimeOffset.UtcNow,
            LastQuerySuccessAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        };

        await viewModel.InitializeAsync();

        Assert.Equal(2, viewModel.TotalAccountCount);
        Assert.Equal(1, viewModel.LowBalanceAccountCount);
        Assert.Equal(1, viewModel.FailedAccountCount);
        Assert.Contains("共 2 个账户", viewModel.AccountSummaryText);
        Assert.Contains("低余额 1", viewModel.AccountSummaryText);
        Assert.Contains("查询失败 1", viewModel.AccountSummaryText);
    }

    [Fact]
    public async Task FocusAccount_ClearsFiltersAndHighlightsTarget()
    {
        var (viewModel, manager, _, _, _) = CreateSut();
        manager.Accounts.Add(Account("acct-a"));
        manager.Accounts.Add(Account("acct-b"));
        await viewModel.InitializeAsync();

        viewModel.SelectedProviderFilter = "openrouter";
        viewModel.FocusAccount("acct-a");

        Assert.Equal(string.Empty, viewModel.SelectedProviderFilter);
        Assert.Equal(AccountStatusFilter.All, viewModel.SelectedStatusFilter);
        Assert.Equal("acct-a", viewModel.HighlightedAccountId);
        Assert.True(viewModel.Accounts.Single(a => a.Account.AccountId == "acct-a").IsHighlighted);
        Assert.False(viewModel.Accounts.Single(a => a.Account.AccountId == "acct-b").IsHighlighted);
        Assert.Equal(2, viewModel.FilteredAccounts.Count);
    }
}
