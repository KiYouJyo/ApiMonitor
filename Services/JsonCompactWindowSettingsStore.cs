using System.Text.Json;
using System.Text.Json.Serialization;
using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 紧凑窗口设置（compact-window-settings.json）的持久化实现。
/// v0.3.0 起新增，schemaVersion 为 3；文件损坏时备份并恢复默认值，
/// 不影响账户、历史、阈值与凭据。
/// </summary>
public sealed class CompactWindowSettingsStore : ICompactWindowSettingsStore
{
    public const string FileName = "compact-window-settings.json";

    /// <summary>
    /// v0.3.0 设置的 schemaVersion（独立设置文件，从 3 开始，
    /// 与账户/余额文件各自的版本号互不影响）。
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    private readonly string _directory;
    private readonly JsonSerializerOptions _options;

    public CompactWindowSettingsStore(string directory)
    {
        _directory = directory;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task<CompactWindowSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await AtomicJsonFile.ReadOrRecoverAsync(
            _directory,
            FileName,
            _options,
            static () => new CompactWindowSettings(),
            cancellationToken);

        var settings = result.Data;
        if (result.RecoveryMessage is not null || settings.SchemaVersion < CurrentSchemaVersion)
        {
            // 文件损坏恢复或低版本/缺失字段时，一律补齐为当前版本并原子写回。
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
            settings = new CompactWindowSettings();
            await SaveAsync(settings, cancellationToken);
        }

        return Sanitize(settings);
    }

    public Task SaveAsync(CompactWindowSettings settings, CancellationToken cancellationToken)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        return AtomicJsonFile.WriteAsync(
            _directory,
            FileName,
            Sanitize(settings),
            _options,
            cancellationToken);
    }

    private static CompactWindowSettings Sanitize(CompactWindowSettings settings)
    {
        // 防止损坏/极端数值破坏窗口：尺寸与坐标强制为有限数，并给最小尺寸兜底。
        settings.SchemaVersion = CurrentSchemaVersion;
        settings.Width = ClampFinite(settings.Width, CompactWindowDefaults.MinWidth, CompactWindowDefaults.MaxWidth);
        settings.Height = ClampFinite(settings.Height, CompactWindowDefaults.MinHeight, CompactWindowDefaults.MaxHeight);
        settings.X = ClampNullableFinite(settings.X);
        settings.Y = ClampNullableFinite(settings.Y);
        return settings;
    }

    private static double ClampFinite(double value, double min, double max)
    {
        if (!double.IsFinite(value))
        {
            return min;
        }

        return Math.Clamp(value, min, max);
    }

    private static double? ClampNullableFinite(double? value)
    {
        if (value is not { } v || !double.IsFinite(v))
        {
            return null;
        }

        return v;
    }
}

/// <summary>紧凑窗口尺寸的默认值与最小/最大边界。</summary>
public static class CompactWindowDefaults
{
    public const double DefaultWidth = 360;
    public const double DefaultHeight = 240;
    public const double MinWidth = 300;
    public const double MinHeight = 180;
    public const double MaxWidth = 480;
    public const double MaxHeight = 420;
}
