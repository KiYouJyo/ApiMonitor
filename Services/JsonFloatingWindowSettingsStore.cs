using System.Text.Json;
using System.Text.Json.Serialization;
using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 悬浮余额窗设置（floating-window-settings.json）的持久化实现（v0.7.0）。
/// 读取时若新文件不存在而旧 compact-window-settings.json 存在，则做一次性
/// 幂等迁移（复制位置/尺寸/置顶/选中账户），不删除旧文件、不影响主程序启动。
/// 文件损坏时备份并恢复默认值，不影响账户、历史、阈值与凭据。
/// </summary>
public sealed class FloatingWindowSettingsStore : IFloatingWindowSettingsStore
{
    public const string FileName = "floating-window-settings.json";

    /// <summary>v0.6.0 及更早版本的旧设置文件名（只读迁移源）。</summary>
    public const string LegacyFileName = "compact-window-settings.json";

    /// <summary>v0.7.0 新设置文件的 schemaVersion（独立设置文件，从 1 开始）。</summary>
    public const int CurrentSchemaVersion = 2;

    private readonly string _directory;
    private readonly JsonSerializerOptions _options;

    public FloatingWindowSettingsStore(string directory)
    {
        _directory = directory;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task<FloatingWindowSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await MigrateFromLegacyAsync(cancellationToken);

        var result = await AtomicJsonFile.ReadOrRecoverAsync(
            _directory,
            FileName,
            _options,
            static () => new FloatingWindowSettings(),
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
            settings = new FloatingWindowSettings();
            await SaveAsync(settings, cancellationToken);
        }

        return Sanitize(settings);
    }

    public Task SaveAsync(FloatingWindowSettings settings, CancellationToken cancellationToken)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        return AtomicJsonFile.WriteAsync(
            _directory,
            FileName,
            Sanitize(settings),
            _options,
            cancellationToken);
    }

    /// <summary>
    /// 幂等迁移：仅当新文件不存在且旧文件存在时，把旧文件可迁移字段复制到新文件。
    /// 旧文件保持原样（保留用户数据，便于回退）；重复调用无副作用。
    /// </summary>
    private async Task MigrateFromLegacyAsync(CancellationToken cancellationToken)
    {
        string legacyPath = Path.Combine(_directory, LegacyFileName);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        string targetPath = Path.Combine(_directory, FileName);
        if (File.Exists(targetPath))
        {
            return;
        }

        try
        {
            string legacyJson = await File.ReadAllTextAsync(legacyPath, cancellationToken);
            var legacy = JsonSerializer.Deserialize<LegacyCompactWindowSettings>(
                legacyJson,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (legacy is null)
            {
                return;
            }

            var migrated = new FloatingWindowSettings
            {
                IsAlwaysOnTop = legacy.IsAlwaysOnTop,
                SelectedAccountId = legacy.SelectedAccountId,
                Width = legacy.Width,
                Height = legacy.Height,
                X = legacy.X,
                Y = legacy.Y,
                LastDisplayId = legacy.LastDisplayId,
            };
            await SaveAsync(migrated, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 迁移失败不影响主程序启动：下次打开悬浮窗时使用默认设置。
        }
    }

    private static FloatingWindowSettings Sanitize(FloatingWindowSettings settings)
    {
        // 防止损坏/极端数值破坏窗口：尺寸与坐标强制为有限数，并给最小尺寸兜底。
        settings.SchemaVersion = CurrentSchemaVersion;
        settings.Width = ClampFinite(settings.Width, FloatingWindowDefaults.MinWidth, FloatingWindowDefaults.MaxWidth);
        settings.Height = ClampFinite(settings.Height, FloatingWindowDefaults.MinHeight, FloatingWindowDefaults.MaxHeight);
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

    /// <summary>旧设置文件的可迁移字段视图（未知字段忽略）。</summary>
    private sealed class LegacyCompactWindowSettings
    {
        public bool IsAlwaysOnTop { get; set; } = true;

        public string? SelectedAccountId { get; set; }

        public double Width { get; set; } = FloatingWindowDefaults.DefaultWidth;

        public double Height { get; set; } = FloatingWindowDefaults.DefaultHeight;

        public double? X { get; set; }

        public double? Y { get; set; }

        public string? LastDisplayId { get; set; }
    }
}

/// <summary>悬浮余额窗尺寸的默认值与最小/最大边界（比原紧凑窗口更小、更轻）。</summary>
public static class FloatingWindowDefaults
{
    public const double FixedSize = 208;
    public const double DefaultWidth = FixedSize;
    public const double DefaultHeight = FixedSize;
    public const double MinWidth = FixedSize;
    public const double MinHeight = FixedSize;
    public const double MaxWidth = FixedSize;
    public const double MaxHeight = FixedSize;
}
