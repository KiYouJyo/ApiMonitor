using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Services;
using ApiBalanceMonitor.Tests.TestDoubles;
using ApiBalanceMonitor.ViewModels;
using Xunit;

namespace ApiBalanceMonitor.Tests;

public sealed class MainViewModelTests
{
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
            AccountId = "acct-1",
            ProviderId = "deepseek",
            IsAvailable = true,
            RetrievedAt = DateTimeOffset.UtcNow,
            Balances = new[]
            {
                new BalanceAmount
                {
                    Currency = "CNY",
                    TotalBalance = 42.00m,
                    GrantedBalance = 2.00m,
                    ToppedUpBalance = 40.00m,
                },
            },
        };

    private static (
        MainViewModel ViewModel,
        FakeAccountManager Manager,
        FakeDialogService Dialogs,
        FakeClipboardService Clipboard) CreateSut()
    {
        var manager = new FakeAccountManager();
        var dialogs = new FakeDialogService();
        var clipboard = new FakeClipboardService();
        var log = new AppLog(Path.Combine(Path.GetTempPath(), $"abm-log-{Guid.NewGuid():N}"));
        var viewModel = new MainViewModel(manager, dialogs, log, clipboard);
        return (viewModel, manager, dialogs, clipboard);
    }

    [Fact]
    public async Task InitializeAsync_PopulatesAccounts()
    {
        var (viewModel, manager, _, _) = CreateSut();
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
        var (viewModel, manager, _, _) = CreateSut();
        manager.RecoveryMessagesList.Add("accounts.json 内容损坏，已备份并重置。");

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsStatusVisible);
        Assert.Equal(StatusSeverity.Warning, viewModel.StatusSeverity);
        Assert.Contains("恢复", viewModel.StatusTitle);
    }

    [Fact]
    public async Task RefreshSuccess_UpdatesItemAndShowsSuccess()
    {
        var (viewModel, manager, _, _) = CreateSut();
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
        var (viewModel, manager, _, _) = CreateSut();
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
    public async Task RefreshShowsLoadingState_AndPreventsDuplicateCalls()
    {
        var (viewModel, manager, _, _) = CreateSut();
        manager.Accounts.Add(Account());
        manager.RefreshGate = new TaskCompletionSource();
        manager.RefreshResult = BalanceQueryResult.Success(Snapshot());
        await viewModel.InitializeAsync();
        var item = viewModel.Accounts[0];

        var first = viewModel.RefreshAccountAsync(item.Account.AccountId);
        var second = viewModel.RefreshAccountAsync(item.Account.AccountId);
        await Task.Delay(50);

        Assert.True(item.IsRefreshing);
        Assert.Equal(1, manager.RefreshCalls);

        manager.RefreshGate.SetResult();
        await Task.WhenAll(first, second);

        Assert.False(item.IsRefreshing);
        Assert.Equal(1, manager.RefreshCalls);
        Assert.True(item.IsAvailable);
    }

    [Fact]
    public async Task InitializeShowsLoadingState()
    {
        var (viewModel, manager, _, _) = CreateSut();
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
        var (viewModel, manager, dialogs, _) = CreateSut();
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
        var (viewModel, manager, dialogs, _) = CreateSut();
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
        var (viewModel, manager, dialogs, _) = CreateSut();
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
        var (viewModel, manager, _, clipboard) = CreateSut();
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
        var (viewModel, manager, _, clipboard) = CreateSut();
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
        var viewModel = new MainViewModel(manager, dialogs, log, clipboard);
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
}
