using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiMonitor.Services;

/// <summary>应用主题偏好。</summary>
public enum AppThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>语言偏好（null 表示跟随系统）。</summary>
public enum AppLanguagePreference
{
    System,
    ZhCn,
    EnUs,
    JaJp,
}

/// <summary>外观与语言设置（v0.6.0 独立设置文件，不影响既有 schema）。</summary>
public sealed class AppearanceSettingsData
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Theme { get; set; } = nameof(AppThemePreference.System);

    public string Language { get; set; } = nameof(AppLanguagePreference.System);
}

/// <summary>
/// 外观与语言设置的 JSON 持久化（v0.6.0）。
/// 独立文件 appearance-settings.json，不修改既有账户/余额/托盘文件结构。
/// 读取失败时回退默认值（跟随系统），绝不阻止账户加载。
/// </summary>
public interface IAppearanceSettingsStore
{
    Task<AppearanceSettingsData> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppearanceSettingsData settings, CancellationToken cancellationToken);
}

public sealed class JsonAppearanceSettingsStore : IAppearanceSettingsStore
{
    public const string FileName = "appearance-settings.json";

    private readonly string _directory;
    private readonly JsonSerializerOptions _options;
    // 同一文件 Load/Save 串行化，避免并发读写互相锁文件（v0.6.0 修复）。
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonAppearanceSettingsStore(string directory)
    {
        _directory = directory;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task<AppearanceSettingsData> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var result = await AtomicJsonFile.ReadOrRecoverAsync(
                _directory,
                FileName,
                _options,
                static () => new AppearanceSettingsData(),
                cancellationToken);

            // 只接受已知枚举值；未知/未来值回退默认，避免升级后崩溃。
            var data = result.Data;
            data.Theme = Enum.TryParse<AppThemePreference>(data.Theme, ignoreCase: true, out var theme)
                ? theme.ToString()
                : nameof(AppThemePreference.System);
            data.Language = Enum.TryParse<AppLanguagePreference>(data.Language, ignoreCase: true, out var language)
                ? language.ToString()
                : nameof(AppLanguagePreference.System);
            return data;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppearanceSettingsData settings, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            settings.SchemaVersion = AppearanceSettingsData.CurrentSchemaVersion;
            await AtomicJsonFile.WriteAsync(_directory, FileName, settings, _options, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
