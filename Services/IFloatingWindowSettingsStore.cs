using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>悬浮余额窗设置的持久化接口（独立于账户/历史/阈值/凭据）。</summary>
public interface IFloatingWindowSettingsStore
{
    Task<FloatingWindowSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(FloatingWindowSettings settings, CancellationToken cancellationToken);
}
