using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>紧凑窗口设置的持久化接口（独立于账户/历史/阈值/凭据）。</summary>
public interface ICompactWindowSettingsStore
{
    Task<CompactWindowSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(CompactWindowSettings settings, CancellationToken cancellationToken);
}
