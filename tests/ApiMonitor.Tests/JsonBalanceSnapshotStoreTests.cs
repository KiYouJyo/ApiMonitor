using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class JsonBalanceSnapshotStoreTests
{
    private static AccountBalanceRecord CreateRecord() =>
        new()
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            LastQueryAttemptAt = new DateTimeOffset(2026, 8, 2, 3, 0, 0, TimeSpan.Zero),
            LastQuerySuccessAt = new DateTimeOffset(2026, 8, 2, 3, 0, 5, TimeSpan.Zero),
            LastSuccessfulSnapshot = new BalanceSnapshot
            {
                SnapshotId = "snap-1",
                AccountId = "acct-1",
                ProviderId = "deepseek",
                IsAvailable = true,
                RetrievedAt = new DateTimeOffset(2026, 8, 2, 3, 0, 5, TimeSpan.Zero),
                Metrics = new[]
                {
                    new BalanceMetric
                    {
                        MetricId = "deepseek:CNY:total",
                        DisplayName = "CNY 总余额",
                        Unit = "CNY",
                        Kind = BalanceMetricKind.MonetaryBalance,
                        AvailableAmount = 110.00m,
                        TotalAmount = 110.00m,
                        GrantedAmount = 10.00m,
                        ToppedUpAmount = 100.00m,
                        IsThresholdSupported = true,
                    },
                    new BalanceMetric
                    {
                        MetricId = "deepseek:USD:total",
                        DisplayName = "USD 总余额",
                        Unit = "USD",
                        Kind = BalanceMetricKind.MonetaryBalance,
                        AvailableAmount = 5.50m,
                        TotalAmount = 5.50m,
                        ToppedUpAmount = 5.50m,
                        IsThresholdSupported = true,
                    },
                },
            },
        };

    [Fact]
    public async Task SaveAndReload_RoundTripsRecordWithSnapshot()
    {
        using var temp = new TempDirectory();
        var store = new JsonBalanceSnapshotStore(temp.Path);

        await store.SaveAsync(new[] { CreateRecord() }, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded.RecoveryMessage);
        var record = Assert.Single(loaded.Records);
        Assert.Equal("acct-1", record.AccountId);
        Assert.NotNull(record.LastQueryAttemptAt);
        Assert.NotNull(record.LastQuerySuccessAt);

        var snapshot = Assert.IsType<BalanceSnapshot>(record.LastSuccessfulSnapshot);
        Assert.True(snapshot.IsAvailable);
        Assert.Equal(2, snapshot.Metrics.Count);
        Assert.Contains(snapshot.Metrics, b => b.MetricId == "deepseek:CNY:total" && b.AvailableAmount == 110.00m);
        Assert.Contains(snapshot.Metrics, b => b.MetricId == "deepseek:USD:total" && b.ToppedUpAmount == 5.50m);
    }

    [Fact]
    public async Task RecordWithoutSnapshot_RoundTripsNulls()
    {
        using var temp = new TempDirectory();
        var store = new JsonBalanceSnapshotStore(temp.Path);
        var record = new AccountBalanceRecord
        {
            AccountId = "acct-2",
            ProviderId = "deepseek",
        };

        await store.SaveAsync(new[] { record }, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        var reloaded = Assert.Single(loaded.Records);
        Assert.Null(reloaded.LastQueryAttemptAt);
        Assert.Null(reloaded.LastQuerySuccessAt);
        Assert.Null(reloaded.LastSuccessfulSnapshot);
    }

    [Fact]
    public async Task MissingFile_ReturnsEmptyRecords()
    {
        using var temp = new TempDirectory();
        var store = new JsonBalanceSnapshotStore(temp.Path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Empty(loaded.Records);
        Assert.Null(loaded.RecoveryMessage);
    }

    [Fact]
    public async Task CorruptFile_ReturnsRecoveryMessageAndBacksUp()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "balance-records.json");
        await File.WriteAllTextAsync(path, "####");

        var store = new JsonBalanceSnapshotStore(temp.Path);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Empty(loaded.Records);
        Assert.NotNull(loaded.RecoveryMessage);
        Assert.Single(Directory.GetFiles(temp.Path, "*.corrupt-*.json"));
    }
}
