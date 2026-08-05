using ApiMonitor.Models;
using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v0.9.0 服务健康通知评估测试（纯逻辑，不调用 Windows API）。
/// </summary>
public sealed class GeospatialNotificationTests
{
    private static ApiAccount Account(bool notificationsEnabled = true) =>
        new()
        {
            AccountId = "acct-amap",
            ProviderId = "amap",
            DisplayName = "AMap",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Notification = new AccountNotificationSettings
            {
                NotificationsEnabled = notificationsEnabled,
                RecoveryNotificationsEnabled = true,
            },
        };

    private static NotificationGlobalSettings Global(bool enabled = true) =>
        new()
        {
            BalanceNotificationsEnabled = enabled,
            RecoveryNotificationsEnabled = true,
            DefaultRepeatIntervalHours = 24,
        };

    private static BalanceSnapshot Snapshot(GeospatialStatus status, bool expectedMissing = false)
    {
        var metrics = GeospatialMetricFactory.BuildMapMetricSet("amap", status, 42L).ToList();
        if (expectedMissing)
        {
            metrics.Add(GeospatialMetricFactory.BooleanMetric(
                "amap",
                "expected-service.present",
                "Metric.ExpectedServiceName",
                false));
        }

        return new BalanceSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            AccountId = "acct-amap",
            ProviderId = "amap",
            IsAvailable = status == GeospatialStatus.Healthy,
            RetrievedAt = DateTimeOffset.UtcNow,
            Metrics = metrics,
        };
    }

    private static NotificationDecision Evaluate(
        ApiAccount account,
        BalanceSnapshot snapshot,
        IReadOnlyList<NotificationStateEntry> states) =>
        new NotificationPolicyEvaluator().Evaluate(
            account,
            snapshot,
            Global(),
            states,
            DateTimeOffset.UtcNow);

    [Fact]
    public void DeterministicProblem_NotifiesOnFirstOccurrence()
    {
        var decision = Evaluate(
            Account(),
            Snapshot(GeospatialStatus.CredentialInvalid),
            Array.Empty<NotificationStateEntry>());

        Assert.True(decision.ShouldNotifyHealth);
        Assert.Equal(HealthNotificationType.CredentialInvalid, decision.HealthItems![0].Type);
    }

    [Fact]
    public void SameDeterministicProblem_DoesNotNotifyTwice()
    {
        var snapshot = Snapshot(GeospatialStatus.CredentialInvalid);
        var evaluator = new NotificationPolicyEvaluator();
        var first = evaluator.Evaluate(Account(), snapshot, Global(), Array.Empty<NotificationStateEntry>(), DateTimeOffset.UtcNow);

        var second = evaluator.Evaluate(Account(), snapshot, Global(), first.UpdatedStates, DateTimeOffset.UtcNow);

        Assert.True(first.ShouldNotifyHealth);
        Assert.False(second.ShouldNotifyHealth);
    }

    [Fact]
    public void TransientProblem_RequiresTwoConsecutiveOccurrences()
    {
        var evaluator = new NotificationPolicyEvaluator();
        var account = Account();
        var states = Array.Empty<NotificationStateEntry>();

        var first = evaluator.Evaluate(
            account,
            Snapshot(GeospatialStatus.Timeout),
            Global(),
            states,
            DateTimeOffset.UtcNow);
        Assert.False(first.ShouldNotifyHealth);

        var second = evaluator.Evaluate(
            account,
            Snapshot(GeospatialStatus.Timeout),
            Global(),
            first.UpdatedStates,
            DateTimeOffset.UtcNow);
        Assert.True(second.ShouldNotifyHealth);
        Assert.Equal(HealthNotificationType.ServiceUnavailable, second.HealthItems![0].Type);
    }

    [Fact]
    public void Recovery_AfterProblem_SendsRecoveryNotification()
    {
        var evaluator = new NotificationPolicyEvaluator();
        var account = Account();
        var problem = evaluator.Evaluate(
            account,
            Snapshot(GeospatialStatus.QuotaExceeded),
            Global(),
            Array.Empty<NotificationStateEntry>(),
            DateTimeOffset.UtcNow);

        var recovery = evaluator.Evaluate(
            account,
            Snapshot(GeospatialStatus.Healthy),
            Global(),
            problem.UpdatedStates,
            DateTimeOffset.UtcNow);

        Assert.True(recovery.ShouldNotifyHealth);
        Assert.Equal(HealthNotificationType.ServiceRecovered, recovery.HealthItems![0].Type);
    }

    [Fact]
    public void ExpectedServiceMissing_NotifiesImmediately()
    {
        var decision = Evaluate(
            Account(),
            Snapshot(GeospatialStatus.Healthy, expectedMissing: true),
            Array.Empty<NotificationStateEntry>());

        Assert.True(decision.ShouldNotifyHealth);
        Assert.Equal(HealthNotificationType.ExpectedServiceMissing, decision.HealthItems![0].Type);
    }

    [Fact]
    public void NotificationsDisabled_SuppressesHealthNotification()
    {
        var decision = Evaluate(
            Account(notificationsEnabled: false),
            Snapshot(GeospatialStatus.CredentialInvalid),
            Array.Empty<NotificationStateEntry>());

        Assert.True(decision.NotificationsSuppressed);
        Assert.False(decision.ShouldNotifyHealth);
    }

    [Fact]
    public void FailureEvaluation_DeterministicError_NotifiesOnFirst()
    {
        var evaluator = new NotificationPolicyEvaluator();
        var error = new BalanceQueryError(BalanceErrorKind.CredentialInvalid, "bad key");

        var decision = evaluator.EvaluateFailure(
            Account(),
            error,
            Global(),
            Array.Empty<NotificationStateEntry>(),
            DateTimeOffset.UtcNow);

        Assert.True(decision.ShouldNotifyHealth);
        Assert.Equal(HealthNotificationType.CredentialInvalid, decision.HealthItems![0].Type);
    }

    [Fact]
    public void FailureEvaluation_TransientError_RequiresTwo()
    {
        var evaluator = new NotificationPolicyEvaluator();
        var error = new BalanceQueryError(BalanceErrorKind.Network, "dns");

        var first = evaluator.EvaluateFailure(
            Account(),
            error,
            Global(),
            Array.Empty<NotificationStateEntry>(),
            DateTimeOffset.UtcNow);
        Assert.False(first.ShouldNotifyHealth);

        var second = evaluator.EvaluateFailure(
            Account(),
            error,
            Global(),
            first.UpdatedStates,
            DateTimeOffset.UtcNow);
        Assert.True(second.ShouldNotifyHealth);
        Assert.Equal(HealthNotificationType.ServiceUnavailable, second.HealthItems![0].Type);
    }
}
