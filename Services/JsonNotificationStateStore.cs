using System.Text.Json;
using System.Text.Json.Serialization;
using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 通知状态（notification-state.json）持久化实现。
/// 存储每个 AccountId+MetricId 的评估/通知/暂停状态，不含任何凭据内容。
/// </summary>
public sealed class JsonNotificationStateStore : INotificationStateStore
{
    public const string FileName = "notification-state.json";
    public const int CurrentSchemaVersion = 1;

    private readonly string _directory;
    private readonly JsonSerializerOptions _options;

    public JsonNotificationStateStore(string directory)
    {
        _directory = directory;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task<IReadOnlyList<NotificationStateEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await AtomicJsonFile.ReadOrRecoverAsync(
            _directory,
            FileName,
            _options,
            static () => new NotificationStateFileData(),
            cancellationToken);

        return result.Data.States
            .Where(s => !string.IsNullOrWhiteSpace(s.AccountId) && !string.IsNullOrWhiteSpace(s.MetricId))
            .Select(s => new NotificationStateEntry
            {
                AccountId = s.AccountId,
                MetricId = s.MetricId,
                LastEvaluatedSnapshotId = s.LastEvaluatedSnapshotId,
                LastState = Enum.TryParse<NotificationStateKind>(s.LastState, ignoreCase: true, out var state)
                    ? state
                    : NotificationStateKind.Unknown,
                LastNotifiedAt = s.LastNotifiedAt,
                LastRecoveryNotifiedAt = s.LastRecoveryNotifiedAt,
                SnoozedUntil = s.SnoozedUntil,
                LastNotificationTag = s.LastNotificationTag,
            })
            .ToList();
    }

    public Task SaveAsync(IReadOnlyList<NotificationStateEntry> states, CancellationToken cancellationToken)
    {
        var data = new NotificationStateFileData
        {
            SchemaVersion = CurrentSchemaVersion,
            States = states
                .Where(s => !string.IsNullOrWhiteSpace(s.AccountId) && !string.IsNullOrWhiteSpace(s.MetricId))
                .Select(s => new NotificationStateFileEntry
                {
                    AccountId = s.AccountId,
                    MetricId = s.MetricId,
                    LastEvaluatedSnapshotId = s.LastEvaluatedSnapshotId,
                    LastState = s.LastState.ToString(),
                    LastNotifiedAt = s.LastNotifiedAt,
                    LastRecoveryNotifiedAt = s.LastRecoveryNotifiedAt,
                    SnoozedUntil = s.SnoozedUntil,
                    LastNotificationTag = s.LastNotificationTag,
                })
                .ToList(),
        };
        return AtomicJsonFile.WriteAsync(_directory, FileName, data, _options, cancellationToken);
    }

    public async Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        var states = (await LoadAsync(cancellationToken))
            .Where(s => !string.Equals(s.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await SaveAsync(states, cancellationToken);
    }
}

/// <summary>notification-state.json 的序列化模型。</summary>
public sealed class NotificationStateFileData
{
    public int SchemaVersion { get; set; } = JsonNotificationStateStore.CurrentSchemaVersion;

    public List<NotificationStateFileEntry> States { get; set; } = new();
}

public sealed class NotificationStateFileEntry
{
    public string AccountId { get; set; } = string.Empty;

    public string MetricId { get; set; } = string.Empty;

    public string? LastEvaluatedSnapshotId { get; set; }

    public string LastState { get; set; } = nameof(NotificationStateKind.Unknown);

    public DateTimeOffset? LastNotifiedAt { get; set; }

    public DateTimeOffset? LastRecoveryNotifiedAt { get; set; }

    public DateTimeOffset? SnoozedUntil { get; set; }

    public string? LastNotificationTag { get; set; }
}
