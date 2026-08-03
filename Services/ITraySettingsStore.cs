using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>托盘与启动设置的持久化接口（独立于账户/历史/阈值/凭据）。</summary>
public interface ITraySettingsStore
{
    Task<TraySettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(TraySettings settings, CancellationToken cancellationToken);
}
