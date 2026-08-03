using System.Text.Json;
using System.Text.Json.Serialization;
using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 全局通知设置（notification-settings.json）持久化实现。
/// v0.5.0 新增；文件缺失时返回“系统提醒关闭、恢复提醒开启、间隔 24 小时”的默认值，
/// 保证升级后不会突然开始弹出提醒。
/// </summary>
public sealed class JsonNotificationSettingsStore : INotificationSettingsStore
{
    public const string FileName = "notification-settings.json";
    public const int CurrentSchemaVersion = 1;

    private readonly string _directory;
    private readonly JsonSerializerOptions _options;

    public JsonNotificationSettingsStore(string directory)
    {
        _directory = directory;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task<NotificationGlobalSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await AtomicJsonFile.ReadOrRecoverAsync(
            _directory,
            FileName,
            _options,
            static () => new NotificationGlobalSettings(),
            cancellationToken);

        var settings = result.Data;
        if (result.RecoveryMessage is not null)
        {
            await SaveAsync(settings, cancellationToken);
        }

        return Sanitize(settings);
    }

    public Task SaveAsync(NotificationGlobalSettings settings, CancellationToken cancellationToken)
    {
        var data = new NotificationSettingsFileData
        {
            SchemaVersion = CurrentSchemaVersion,
            Settings = Sanitize(settings),
        };
        return AtomicJsonFile.WriteAsync(_directory, FileName, data, _options, cancellationToken);
    }

    private static NotificationGlobalSettings Sanitize(NotificationGlobalSettings settings)
    {
        if (!NotificationRepeatIntervals.Options.Contains(settings.DefaultRepeatIntervalHours))
        {
            settings.DefaultRepeatIntervalHours = NotificationRepeatIntervals.DefaultHours;
        }

        return settings;
    }
}

/// <summary>notification-settings.json 的序列化模型。</summary>
public sealed class NotificationSettingsFileData
{
    public int SchemaVersion { get; set; } = JsonNotificationSettingsStore.CurrentSchemaVersion;

    public NotificationGlobalSettings Settings { get; set; } = new();
}
