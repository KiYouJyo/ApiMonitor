using ApiMonitor.Models;

namespace ApiMonitor.Services;

public interface INotificationStateStore
{
    Task<IReadOnlyList<NotificationStateEntry>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyList<NotificationStateEntry> states, CancellationToken cancellationToken);

    Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken);
}

public interface INotificationSettingsStore
{
    Task<NotificationGlobalSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(NotificationGlobalSettings settings, CancellationToken cancellationToken);
}
