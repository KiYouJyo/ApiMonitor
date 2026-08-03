using ApiMonitor.Models;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class BalanceHistoryViewModelTests
{
    private static BalanceHistoryEntry Entry(string id, DateTimeOffset at, BalanceQuerySource source) =>
        new()
        {
            Id = id,
            AccountId = "acct-1",
            ProviderId = "deepseek",
            SucceededAtUtc = at,
            Source = source,
            IsAvailable = true,
            Balances = new[]
            {
                new BalanceAmount
                {
                    Currency = "CNY",
                    TotalBalance = 100m,
                    GrantedBalance = 10m,
                    ToppedUpBalance = 90m,
                },
            },
        };

    [Fact]
    public async Task LoadAsync_PopulatesItemsInOrderAndEmptyState()
    {
        var manager = new FakeAccountManager();
        var viewModel = new BalanceHistoryViewModel(manager, "acct-1");
        manager.HistoryResult.AddRange(new[]
        {
            Entry("1", new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero), BalanceQuerySource.Manual),
            Entry("2", new DateTimeOffset(2026, 8, 3, 1, 0, 0, TimeSpan.Zero), BalanceQuerySource.Automatic),
        });

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasItems);
        Assert.Equal(2, viewModel.Items.Count);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 3, 1, 0, 0, TimeSpan.Zero).ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            viewModel.Items[0].TimeText);
        Assert.Equal("自动", viewModel.Items[0].SourceText);
        Assert.Contains("总额 100.00", viewModel.Items[0].BalanceLines[0]);
        Assert.Equal("手动", viewModel.Items[1].SourceText);
    }

    [Fact]
    public async Task LoadAsync_EmptyHistory_ShowsEmptyState()
    {
        var manager = new FakeAccountManager();
        var viewModel = new BalanceHistoryViewModel(manager, "acct-1");

        await viewModel.LoadAsync();

        Assert.False(viewModel.HasItems);
        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public async Task ConfirmClear_InvokesManagerAndReloads()
    {
        var manager = new FakeAccountManager();
        manager.HistoryResult.Add(Entry("1", DateTimeOffset.UtcNow, BalanceQuerySource.Manual));
        var viewModel = new BalanceHistoryViewModel(manager, "acct-1");
        await viewModel.LoadAsync();

        viewModel.BeginClearCommand.Execute(null);
        Assert.True(viewModel.IsConfirmingClear);
        await viewModel.ConfirmClearCommand.ExecuteAsync(null);

        Assert.Equal(1, manager.ClearHistoryCalls);
        Assert.False(viewModel.IsConfirmingClear);
        Assert.False(viewModel.HasItems);
    }

    [Fact]
    public async Task CancelClear_DoesNotClear()
    {
        var manager = new FakeAccountManager();
        var viewModel = new BalanceHistoryViewModel(manager, "acct-1");
        await viewModel.LoadAsync();

        viewModel.BeginClearCommand.Execute(null);
        viewModel.CancelClearCommand.Execute(null);

        Assert.False(viewModel.IsConfirmingClear);
        Assert.Equal(0, manager.ClearHistoryCalls);
    }
}
