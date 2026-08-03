using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.ViewModels;
using Xunit;

namespace ApiBalanceMonitor.Tests;

public sealed class AccountListItemViewModelTests
{
    private static ApiAccount Account(bool hasCredential = true) =>
        new()
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "测试账户",
            HasCredential = hasCredential,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static BalanceSnapshot Snapshot(DateTimeOffset retrievedAt) =>
        new()
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            IsAvailable = true,
            RetrievedAt = retrievedAt,
            Balances = Array.Empty<BalanceAmount>(),
        };

    private static AccountListItemViewModel CreateItem(AccountBalanceRecord? record = null) =>
        new(
            Account(),
            "DeepSeek",
            record,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask);

    [Fact]
    public void WithoutSuccessRecord_ShowsPlaceholder()
    {
        var item = CreateItem();

        Assert.Equal("最近成功更新：尚未成功更新", item.LastSuccessLine);
    }

    [Fact]
    public void WithSnapshot_ShowsLocalTimeInStableFormat()
    {
        var retrievedAt = new DateTimeOffset(2026, 8, 3, 0, 30, 0, TimeSpan.Zero);
        var item = CreateItem();

        item.ApplySnapshot(Snapshot(retrievedAt));

        string expected = "最近成功更新：" + retrievedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        Assert.Equal(expected, item.LastSuccessLine);
    }

    [Fact]
    public void RefreshSuccess_UpdatesLastSuccessLine()
    {
        var item = CreateItem();
        var first = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 8, 3, 1, 15, 0, TimeSpan.Zero);

        item.ApplySnapshot(Snapshot(first));
        string before = item.LastSuccessLine;

        item.ApplySnapshot(Snapshot(second));

        Assert.NotEqual(before, item.LastSuccessLine);
        Assert.Equal(
            "最近成功更新：" + second.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            item.LastSuccessLine);
    }

    [Fact]
    public void RefreshFailure_DoesNotClearLastSuccessTime()
    {
        var retrievedAt = new DateTimeOffset(2026, 8, 3, 0, 45, 0, TimeSpan.Zero);
        var item = CreateItem();
        item.ApplySnapshot(Snapshot(retrievedAt));
        string before = item.LastSuccessLine;

        item.ApplyError(new BalanceQueryError(BalanceErrorKind.Unauthorized, "API Key 无效"));

        Assert.Equal(before, item.LastSuccessLine);
        Assert.Contains("API Key 无效", item.LastErrorText);
    }

    [Fact]
    public void CopyKeyCommand_DisabledWhenAccountHasNoCredential()
    {
        var item = new AccountListItemViewModel(
            Account(hasCredential: false),
            "DeepSeek",
            null,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask);

        Assert.False(item.CopyKeyCommand.CanExecute(null));
    }
}
