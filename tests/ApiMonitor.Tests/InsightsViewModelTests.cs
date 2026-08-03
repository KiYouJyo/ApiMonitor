using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class InsightsViewModelTests
{
    private sealed class FakeHistoryProvider : IInsightsHistoryProvider
    {
        public List<BalanceHistoryEntry> History { get; } = new();

        public Task<IReadOnlyList<BalanceHistoryEntry>> GetHistoryAsync(
            string accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BalanceHistoryEntry>>(History);
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        public string? SavePath { get; set; }

        public Task<string?> PickSaveFileAsync(
            string suggestedFileName,
            IReadOnlyList<string> extensions,
            CancellationToken cancellationToken) =>
            Task.FromResult(SavePath);

        public Task<string?> PickOpenFileAsync(
            IReadOnlyList<string> extensions,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private static BalanceHistoryEntry Entry(
        int index,
        int daysAgo,
        decimal? value,
        string metricId = "deepseek:CNY:total",
        BalanceQuerySource source = BalanceQuerySource.Manual) =>
        new()
        {
            Id = $"id-{index:D4}",
            AccountId = "acct-1",
            ProviderId = "deepseek",
            SucceededAtUtc = DateTimeOffset.UtcNow.AddDays(-daysAgo),
            Source = source,
            IsAvailable = true,
            Metrics = new[]
            {
                new BalanceMetric
                {
                    MetricId = metricId,
                    DisplayName = "CNY 总余额",
                    Unit = "CNY",
                    Kind = BalanceMetricKind.MonetaryBalance,
                    AvailableAmount = value,
                },
            },
        };

    private static InsightsViewModel CreateVm(
        FakeAccountManager accountManager,
        FakeHistoryProvider history,
        FakeFilePicker picker) =>
        new(
            accountManager,
            history,
            new TrendDataBuilder(),
            new ConsumptionEstimateService(),
            new CsvHistoryExporter(),
            picker);

    [Fact]
    public async Task LoadAccounts_PopulatesAccountOptions()
    {
        var accountManager = new FakeAccountManager();
        accountManager.Accounts.Add(new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "我的 DeepSeek",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        var vm = CreateVm(accountManager, new FakeHistoryProvider(), new FakeFilePicker());
        await vm.LoadAccountsAsync();

        var option = Assert.Single(vm.Accounts);
        Assert.Equal("acct-1", option.AccountId);
    }

    [Fact]
    public async Task SelectAccount_LoadsMetricsAndData()
    {
        var accountManager = new FakeAccountManager();
        accountManager.Accounts.Add(new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "我的 DeepSeek",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        var history = new FakeHistoryProvider();
        history.History.AddRange(new[]
        {
            Entry(1, 1, 100m),
            Entry(2, 2, 90m),
            Entry(3, 3, 80m),
        });

        var vm = CreateVm(accountManager, history, new FakeFilePicker());
        await vm.LoadAccountsAsync();
        vm.SelectAccount("acct-1");
        await Task.Delay(100); // 等待异步加载

        Assert.Single(vm.Metrics);
        Assert.Equal("deepseek:CNY:total", vm.Metrics[0].MetricId);
        Assert.True(vm.HasData);
        Assert.True(vm.TrendPoints.Count >= 3);
        Assert.Equal("100 CNY", vm.CurrentValueText); // daysAgo=1 是最新点
    }

    [Fact]
    public async Task NoHistory_ShowsEmptyState()
    {
        var accountManager = new FakeAccountManager();
        accountManager.Accounts.Add(new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "我的 DeepSeek",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        var vm = CreateVm(accountManager, new FakeHistoryProvider(), new FakeFilePicker());
        await vm.LoadAccountsAsync();
        vm.SelectAccount("acct-1");
        await Task.Delay(100);

        Assert.False(vm.HasData);
        Assert.Contains("尚无足够的历史数据", vm.EmptyMessage);
    }

    [Fact]
    public async Task UnknownValues_DoNotBecomeZero()
    {
        var accountManager = new FakeAccountManager();
        accountManager.Accounts.Add(new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "我的 DeepSeek",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        var history = new FakeHistoryProvider();
        history.History.AddRange(new[]
        {
            Entry(1, 1, null),
            Entry(2, 2, 90m),
            Entry(3, 3, 80m),
        });

        var vm = CreateVm(accountManager, history, new FakeFilePicker());
        await vm.LoadAccountsAsync();
        vm.SelectAccount("acct-1");
        await Task.Delay(100);

        // 未知值点存在且为 null（不当作 0）。
        Assert.Contains(vm.TrendPoints, p => p.Value is null);
    }

    [Fact]
    public async Task ExportCsv_WritesFile()
    {
        using var temp = new ApiMonitor.Tests.TestHelpers.TempDirectory();
        var accountManager = new FakeAccountManager();
        accountManager.Accounts.Add(new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "我的 DeepSeek",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        var history = new FakeHistoryProvider();
        history.History.Add(Entry(1, 1, 100m));

        var picker = new FakeFilePicker { SavePath = Path.Combine(temp.Path, "out.csv") };
        var vm = CreateVm(accountManager, history, picker);
        await vm.LoadAccountsAsync();
        vm.SelectAccount("acct-1");
        await Task.Delay(100);

        await vm.ExportCsvCommand.ExecuteAsync(null);

        Assert.True(File.Exists(picker.SavePath));
        string content = File.ReadAllText(picker.SavePath!);
        Assert.Contains("TimestampUtc", content);
        Assert.Contains("deepseek:CNY:total", content);
    }

    [Fact]
    public async Task Release_ClearsLargeCollections()
    {
        var accountManager = new FakeAccountManager();
        accountManager.Accounts.Add(new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "我的 DeepSeek",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        var history = new FakeHistoryProvider();
        history.History.AddRange(new[]
        {
            Entry(1, 1, 100m),
            Entry(2, 2, 90m),
        });

        var vm = CreateVm(accountManager, history, new FakeFilePicker());
        await vm.LoadAccountsAsync();
        vm.SelectAccount("acct-1");
        await Task.Delay(100);
        Assert.True(vm.HasData);

        vm.Release();

        Assert.False(vm.HasData);
        Assert.Empty(vm.TrendPoints);
        Assert.Empty(vm.HistoryRows);
    }
}
