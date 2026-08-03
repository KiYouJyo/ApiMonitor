using System.Globalization;
using ApiMonitor.Models;
using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class CsvHistoryExporterTests
{
    private static readonly ICsvHistoryExporter Exporter = new CsvHistoryExporter();

    private static BalanceHistoryEntry Entry(
        string id,
        DateTimeOffset at,
        string accountId = "acct-1",
        string providerId = "deepseek",
        BalanceQuerySource source = BalanceQuerySource.Manual,
        decimal? available = 100m,
        string metricId = "deepseek:CNY:total") =>
        new()
        {
            Id = id,
            AccountId = accountId,
            ProviderId = providerId,
            SucceededAtUtc = at,
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
                    AvailableAmount = available,
                },
            },
        };

    [Fact]
    public async Task Export_HasBomAndHeader()
    {
        var csv = await Exporter.ExportAsync(
            new[] { Entry("id-1", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)) },
            new Dictionary<string, ApiAccount>(),
            CancellationToken.None);

        Assert.StartsWith("\uFEFF", csv);
        Assert.Contains("TimestampUtc,AccountId,AccountDisplayName,ProviderId,MetricId,MetricDisplayName,Unit,AvailableAmount,TotalAmount,UsedAmount,GrantedAmount,ToppedUpAmount,QuerySource", csv);
    }

    [Fact]
    public async Task Export_FormatsTimestampIso8601Utc()
    {
        var at = new DateTimeOffset(2026, 8, 1, 12, 30, 0, TimeSpan.FromHours(8)); // UTC 04:30
        var csv = await Exporter.ExportAsync(
            new[] { Entry("id-1", at) },
            new Dictionary<string, ApiAccount>(),
            CancellationToken.None);

        Assert.Contains("2026-08-01T04:30:00", csv);
    }

    [Fact]
    public async Task Export_UnknownValue_IsEmpty_NotZero()
    {
        var csv = await Exporter.ExportAsync(
            new[] { Entry("id-1", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), available: null) },
            new Dictionary<string, ApiAccount>(),
            CancellationToken.None);

        string line = csv.Split('\n')[1];
        string[] fields = line.Split(',');
        Assert.Equal(string.Empty, fields[7]); // AvailableAmount 为空，不是 "0"
    }

    [Fact]
    public async Task Export_EscapesCommaQuoteAndNewline()
    {
        var account = new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "账户,\"引号\"\n换行",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var csv = await Exporter.ExportAsync(
            new[] { Entry("id-1", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)) },
            new Dictionary<string, ApiAccount> { ["acct-1"] = account },
            CancellationToken.None);

        Assert.Contains("\"账户,\"\"引号\"\"\n换行\"", csv);
    }

    [Fact]
    public async Task Export_DoesNotContainSecrets()
    {
        var csv = await Exporter.ExportAsync(
            new[] { Entry("id-1", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)) },
            new Dictionary<string, ApiAccount>(),
            CancellationToken.None);

        Assert.DoesNotContain("sk-", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credential Locker", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("app.log", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\", csv, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_UsesInvariantCultureForDecimals()
    {
        var csv = await Exporter.ExportAsync(
            new[] { Entry("id-1", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), available: 1234.56m) },
            new Dictionary<string, ApiAccount>(),
            CancellationToken.None);

        Assert.Contains("1234.56", csv); // 小数点固定为 '.'（invariant）
        Assert.DoesNotContain("1234,56", csv);
    }

    [Fact]
    public async Task Export_SortsByTimeAscending()
    {
        var csv = await Exporter.ExportAsync(
            new[]
            {
                Entry("id-2", new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero)),
                Entry("id-1", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            },
            new Dictionary<string, ApiAccount>(),
            CancellationToken.None);

        int first = csv.IndexOf("2026-08-01", StringComparison.Ordinal);
        int second = csv.IndexOf("2026-08-02", StringComparison.Ordinal);
        Assert.True(first >= 0 && second > first);
    }
}
