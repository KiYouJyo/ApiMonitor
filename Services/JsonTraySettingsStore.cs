using System.Text.Json;
using System.Text.Json.Serialization;
using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 托盘与启动设置（tray-settings.json）的持久化实现。
/// v0.4.0 新增，schemaVersion 为 4（设置体系从 3 升级到 4）；
/// v0.3.1 没有本文件时返回默认值；文件损坏时备份并恢复默认值，
/// 不影响账户、历史、阈值、紧凑窗口设置与凭据。
/// </summary>
public sealed class JsonTraySettingsStore : ITraySettingsStore
{
    public const string FileName = "tray-settings.json";

    /// <summary>
    /// 当前设置版本。设置体系从 v0.3.x 的 3 升级到 4：
    /// 低于 4 或缺失一律补齐为当前版本并原子写回。
    /// </summary>
    public const int CurrentSchemaVersion = 4;

    private readonly string _directory;
    private readonly JsonSerializerOptions _options;

    public JsonTraySettingsStore(string directory)
    {
        _directory = directory;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task<TraySettings> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await AtomicJsonFile.ReadOrRecoverAsync(
            _directory,
            FileName,
            _options,
            static () => new TraySettings(),
            cancellationToken);

        var settings = result.Data;
        if (result.RecoveryMessage is not null || settings.SchemaVersion < CurrentSchemaVersion)
        {
            // 损坏恢复或低版本（含 v0.3.1 无此文件）时，补齐为当前版本并原子写回。
            settings.SchemaVersion = CurrentSchemaVersion;
            await SaveAsync(settings, cancellationToken);
        }
        else if (settings.SchemaVersion > CurrentSchemaVersion)
        {
            string backup = await AtomicJsonFile.BackupCorruptFileAsync(
                _directory,
                FileName,
                cancellationToken);
            _ = backup;
            settings = new TraySettings();
            await SaveAsync(settings, cancellationToken);
        }

        return Sanitize(settings);
    }

    public Task SaveAsync(TraySettings settings, CancellationToken cancellationToken)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        return AtomicJsonFile.WriteAsync(
            _directory,
            FileName,
            Sanitize(settings),
            _options,
            cancellationToken);
    }

    private static TraySettings Sanitize(TraySettings settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        if (!Enum.IsDefined(settings.MainWindowCloseBehavior))
        {
            settings.MainWindowCloseBehavior = MainWindowCloseBehavior.HideToTray;
        }

        if (settings.LastKnownStartupTaskState is { } state
            && !Enum.IsDefined(state))
        {
            settings.LastKnownStartupTaskState = null;
        }

        return settings;
    }
}
