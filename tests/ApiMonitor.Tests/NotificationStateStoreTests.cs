using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class NotificationStateStoreTests
{
    [Fact]
    public async Task SaveAndReload_RoundTripsStates()
    {
        using var temp = new TempDirectory();
        var store = new JsonNotificationStateStore(temp.Path);
        var state = new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastEvaluatedSnapshotId = "snap-1",
            LastState = NotificationStateKind.Low,
            LastNotifiedAt = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
            LastRecoveryNotifiedAt = null,
            SnoozedUntil = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero),
            LastNotificationTag = "ApiMonitor-acct-1",
        };

        await store.SaveAsync(new[] { state }, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        var reloaded = Assert.Single(loaded);
        Assert.Equal(NotificationStateKind.Low, reloaded.LastState);
        Assert.Equal("snap-1", reloaded.LastEvaluatedSnapshotId);
        Assert.Equal(state.SnoozedUntil, reloaded.SnoozedUntil);
        Assert.Equal("ApiMonitor-acct-1", reloaded.LastNotificationTag);

        string json = await File.ReadAllTextAsync(Path.Combine(temp.Path, JsonNotificationStateStore.FileName));
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAccount_RemovesOnlyThatAccount()
    {
        using var temp = new TempDirectory();
        var store = new JsonNotificationStateStore(temp.Path);
        await store.SaveAsync(new[]
        {
            new NotificationStateEntry { AccountId = "a", MetricId = "m1", LastState = NotificationStateKind.Low },
            new NotificationStateEntry { AccountId = "b", MetricId = "m1", LastState = NotificationStateKind.Low },
        }, CancellationToken.None);

        await store.DeleteAccountAsync("a", CancellationToken.None);

        var remaining = await store.LoadAsync(CancellationToken.None);
        Assert.Equal("b", Assert.Single(remaining).AccountId);
    }
}
