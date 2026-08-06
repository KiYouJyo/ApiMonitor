using System.Text.Json;

namespace ApiMonitor.Services;

/// <summary>
/// v1.0.0：首次启动引导状态数据（onboarding.json）。
/// Store 正式身份按全新安装处理：新身份首次启动时该文件不存在 → 显示引导；
/// 完成或跳过后写入 Completed 标记，之后不再自动弹出；设置页可随时重置。
/// 本服务绝不读取旧 Package Family 的 LocalState，不做跨包数据迁移。
/// </summary>
public sealed class OnboardingStateData
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>引导是否已完成（完成或跳过均视为已完成，避免反复弹出）。</summary>
    public bool OnboardingCompleted { get; set; }

    /// <summary>是否通过“跳过”完成。</summary>
    public bool OnboardingSkipped { get; set; }

    /// <summary>完成/跳过时间（UTC）。</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

/// <summary>首次启动引导状态服务。</summary>
public interface IOnboardingStateService
{
    Task<OnboardingStateData> LoadAsync(CancellationToken cancellationToken);

    Task<bool> IsCompletedAsync(CancellationToken cancellationToken);

    /// <summary>标记引导完成（skipped=true 表示用户选择跳过；两者都不再自动弹出）。</summary>
    Task MarkCompletedAsync(bool skipped, CancellationToken cancellationToken);

    /// <summary>重置引导状态（设置页“重新打开首次使用引导”）。</summary>
    Task ResetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// JSON 实现：临时文件 + 原子替换；损坏文件自动备份并回退默认值；
/// schema 升级幂等；不写入任何 API Key 或用户数据。
/// </summary>
public sealed class JsonOnboardingStateStore : IOnboardingStateService
{
    public const string FileName = "onboarding.json";

    private readonly string _directory;
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public JsonOnboardingStateStore(string dataDirectory)
    {
        _directory = dataDirectory;
    }

    public async Task<OnboardingStateData> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await AtomicJsonFile.ReadOrRecoverAsync(
            _directory,
            FileName,
            _options,
            static () => new OnboardingStateData(),
            cancellationToken);
        var data = result.Data;
        if (data.SchemaVersion != OnboardingStateData.CurrentSchemaVersion)
        {
            data.SchemaVersion = OnboardingStateData.CurrentSchemaVersion;
        }

        return data;
    }

    public async Task<bool> IsCompletedAsync(CancellationToken cancellationToken)
    {
        var data = await LoadAsync(cancellationToken);
        return data.OnboardingCompleted;
    }

    public Task MarkCompletedAsync(bool skipped, CancellationToken cancellationToken)
    {
        var data = new OnboardingStateData
        {
            SchemaVersion = OnboardingStateData.CurrentSchemaVersion,
            OnboardingCompleted = true,
            OnboardingSkipped = skipped,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };
        return AtomicJsonFile.WriteAsync(_directory, FileName, data, _options, cancellationToken);
    }

    public Task ResetAsync(CancellationToken cancellationToken)
    {
        var data = new OnboardingStateData
        {
            SchemaVersion = OnboardingStateData.CurrentSchemaVersion,
            OnboardingCompleted = false,
            OnboardingSkipped = false,
            CompletedAtUtc = null,
        };
        return AtomicJsonFile.WriteAsync(_directory, FileName, data, _options, cancellationToken);
    }
}
