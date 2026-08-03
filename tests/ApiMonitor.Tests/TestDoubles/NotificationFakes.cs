using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Tests.TestDoubles;

public sealed class FakeNotificationStateStore : INotificationStateStore
{
    public List<NotificationStateEntry> States { get; } = new();

    public int SaveCalls { get; private set; }

    public List<string> DeletedAccountIds { get; } = new();

    public Task<IReadOnlyList<NotificationStateEntry>> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationStateEntry>>(States.ToList());

    public Task SaveAsync(IReadOnlyList<NotificationStateEntry> states, CancellationToken cancellationToken)
    {
        SaveCalls++;
        States.Clear();
        States.AddRange(states);
        return Task.CompletedTask;
    }

    public Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        DeletedAccountIds.Add(accountId);
        States.RemoveAll(s => string.Equals(s.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }
}

public sealed class FakeNotificationSettingsStore : INotificationSettingsStore
{
    public NotificationGlobalSettings Settings { get; set; } = new();

    public Task<NotificationGlobalSettings> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Settings);

    public Task SaveAsync(NotificationGlobalSettings settings, CancellationToken cancellationToken)
    {
        Settings = settings;
        return Task.CompletedTask;
    }
}

public sealed class FakeAppNotificationService : IAppNotificationService
{
    public bool IsRegistered { get; private set; }

    public int LowNotificationsShown { get; private set; }

    public int RecoveryNotificationsShown { get; private set; }

    public int TestNotificationsShown { get; private set; }

    public List<string> RemovedAccountIds { get; } = new();

    public List<LowBalanceNotificationItem> LastLowItems { get; private set; } = new();

    public string? LastTag { get; private set; }

    public string? LastAccountDisplayName { get; private set; }

    public event EventHandler<NotificationActivationPayload>? Activated;

    public void Register() => IsRegistered = true;

    public void Unregister() => IsRegistered = false;

    public NotificationActivationPayload? DrainInitialPayload() => null;

    public void HandleAppInstanceActivation(object? args)
    {
    }

    public void ShowLowBalance(
        string accountId,
        string providerId,
        string providerDisplayName,
        string accountDisplayName,
        IReadOnlyList<LowBalanceNotificationItem> items,
        string tag)
    {
        LowNotificationsShown++;
        LastLowItems = items.ToList();
        LastTag = tag;
        LastAccountDisplayName = accountDisplayName;
    }

    public void ShowRecovery(
        string accountId,
        string providerId,
        string providerDisplayName,
        string accountDisplayName,
        IReadOnlyList<RecoveryNotificationItem> items,
        string tag)
    {
        RecoveryNotificationsShown++;
        LastTag = tag;
        LastAccountDisplayName = accountDisplayName;
    }

    public void ShowTestNotification() => TestNotificationsShown++;

    public void RemoveAccountNotifications(string accountId) => RemovedAccountIds.Add(accountId);

    public void RaiseActivated(NotificationActivationPayload payload) =>
        Activated?.Invoke(this, payload);
}
