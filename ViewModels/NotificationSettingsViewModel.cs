using ApiMonitor.Models;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>全局余额提醒设置的展示选项。</summary>
public sealed record NotificationRepeatOption(string Label, int Hours);

/// <summary>
/// 主界面“余额提醒”设置区 ViewModel：全局开关、默认重复间隔、
/// 恢复提醒开关与测试通知。测试通知不查询 API、不改变阈值状态、
/// 不写入余额历史。
/// </summary>
public sealed partial class NotificationSettingsViewModel : ObservableObject
{
    private readonly INotificationSettingsStore _settingsStore;
    private readonly Action _sendTestNotification;
    private readonly Action _openWindowsNotificationSettings;
    private readonly AppLog? _log;
    private readonly CancellationTokenSource _lifetime = new();

    public IReadOnlyList<NotificationRepeatOption> RepeatOptions { get; } = new[]
    {
        new NotificationRepeatOption(L10n.Get("Notification.NoRepeat"), NotificationRepeatIntervals.None),
        new NotificationRepeatOption(L10n.Get("Notification.Hours6"), NotificationRepeatIntervals.SixHours),
        new NotificationRepeatOption(L10n.Get("Notification.Hours12"), NotificationRepeatIntervals.TwelveHours),
        new NotificationRepeatOption(L10n.Get("Notification.Hours24"), NotificationRepeatIntervals.DefaultHours),
        new NotificationRepeatOption(L10n.Get("Notification.Days3"), NotificationRepeatIntervals.ThreeDays),
    };

    [ObservableProperty]
    private bool _balanceNotificationsEnabled;

    [ObservableProperty]
    private int _defaultRepeatIntervalHours = NotificationRepeatIntervals.DefaultHours;

    [ObservableProperty]
    private bool _recoveryNotificationsEnabled = true;

    [ObservableProperty]
    private bool _isTestSending;

    [ObservableProperty]
    private bool _hasStatusText;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>设置区固定说明：通知只在本机 ApiMonitor 运行期间生成。</summary>
    public string LocalOnlyExplanationText =>
        L10n.Get("Settings.NotificationLocalOnly");

    public RelayCommand SendTestCommand { get; }

    public RelayCommand OpenWindowsNotificationSettingsCommand { get; }

    public NotificationSettingsViewModel(
        INotificationSettingsStore settingsStore,
        Action sendTestNotification,
        Action openWindowsNotificationSettings,
        AppLog? log = null)
    {
        _settingsStore = settingsStore;
        _sendTestNotification = sendTestNotification;
        _openWindowsNotificationSettings = openWindowsNotificationSettings;
        _log = log;

        SendTestCommand = new RelayCommand(SendTest, () => !IsTestSending);
        OpenWindowsNotificationSettingsCommand = new RelayCommand(
            () => _openWindowsNotificationSettings());
    }

    public async Task InitializeAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync(_lifetime.Token);
            BalanceNotificationsEnabled = settings.BalanceNotificationsEnabled;
            DefaultRepeatIntervalHours = settings.DefaultRepeatIntervalHours;
            RecoveryNotificationsEnabled = settings.RecoveryNotificationsEnabled;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"加载通知设置失败: {ex.GetType().Name}");
        }
    }

    partial void OnBalanceNotificationsEnabledChanged(bool value) =>
        _ = SaveAsync();

    partial void OnDefaultRepeatIntervalHoursChanged(int value) =>
        _ = SaveAsync();

    partial void OnRecoveryNotificationsEnabledChanged(bool value) =>
        _ = SaveAsync();

    partial void OnIsTestSendingChanged(bool value) =>
        SendTestCommand.NotifyCanExecuteChanged();

    private void SendTest()
    {
        IsTestSending = true;
        try
        {
            _sendTestNotification();
            StatusText = L10n.Get("Notification.SentDetailed");
            HasStatusText = true;
        }
        catch (Exception ex)
        {
            _log?.Error($"发送测试通知失败: {ex.GetType().Name}");
            StatusText = L10n.Get("Notification.SendFailedDetailed");
            HasStatusText = true;
        }
        finally
        {
            IsTestSending = false;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync(_lifetime.Token);
            settings.BalanceNotificationsEnabled = BalanceNotificationsEnabled;
            settings.DefaultRepeatIntervalHours = DefaultRepeatIntervalHours;
            settings.RecoveryNotificationsEnabled = RecoveryNotificationsEnabled;
            await _settingsStore.SaveAsync(settings, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"保存通知设置失败: {ex.GetType().Name}");
        }
    }
}
