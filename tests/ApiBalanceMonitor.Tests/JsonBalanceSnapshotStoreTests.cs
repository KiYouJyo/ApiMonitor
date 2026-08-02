using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Services;
using ApiBalanceMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiBalanceMonitor.Tests;

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
                AccountId = "acct-1",
                ProviderId = "deepseek",
                IsAvailable = true,
                RetrievedAt = new DateTimeOffset(2026, 8, 2, 3, 0, 5, TimeSpan.Zero),
                Balances = new[]
                {
                    new BalanceAmount
                    {
                        Currency = "CNY",
                        TotalBalance = 110.00m,
                        GrantedBalance = 10.00m,
                        ToppedUpBalance = 100.00m,
                    },
                    new BalanceAmount
                    {
                        Currency = "USD",
                        TotalBalance = 5.50m,
                        GrantedBalance = 0m,
                        ToppedUpBalance = 5.50m,
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
        Assert.Equal(2, snapshot.Balances.Count);
        Assert.Contains(snapshot.Balances, b => b.Currency == "CNY" && b.TotalBalance == 110.00m);
        Assert.Contains(snapshot.Balances, b => b.Currency == "USD" && b.ToppedUpBalance == 5.50m);
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
