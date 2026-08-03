using System.Collections.ObjectModel;
using ApiMonitor.Helpers;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>添加/编辑账户对话框的 ViewModel（测试连接只预览结果，保存才写入）。</summary>
public sealed partial class AccountEditorViewModel : ObservableObject
{
    private readonly IAccountManager _accountManager;
    private readonly AccountEditorContext _context;

    public IReadOnlyList<ProviderInfo> Providers => _context.Providers;

    public IReadOnlyList<int> RefreshIntervals => MonitoringIntervals.Options;

    /// <summary>通知开关选项（0=继承全局，1=开启，2=关闭）。</summary>
    public sealed record NotificationPreferenceOption(string Label, int Value);

    /// <summary>重复提醒间隔选项（-1=继承全局，其余为小时数）。</summary>
    public sealed record NotificationRepeatOption(string Label, int Value);

    public IReadOnlyList<NotificationPreferenceOption> NotificationEnabledOptions { get; } = new[]
    {
        new NotificationPreferenceOption("继承全局", 0),
        new NotificationPreferenceOption("开启", 1),
        new NotificationPreferenceOption("关闭", 2),
    };

    public IReadOnlyList<NotificationRepeatOption> NotificationRepeatOptions { get; } = new[]
    {
        new NotificationRepeatOption("继承全局", -1),
        new NotificationRepeatOption("不重复", 0),
        new NotificationRepeatOption("6 小时", 6),
        new NotificationRepeatOption("12 小时", 12),
        new NotificationRepeatOption("24 小时", 24),
        new NotificationRepeatOption("3 天", 72),
    };

    public IReadOnlyList<NotificationPreferenceOption> RecoveryOptions { get; } = new[]
    {
        new NotificationPreferenceOption("继承全局", 0),
        new NotificationPreferenceOption("开启", 1),
        new NotificationPreferenceOption("关闭", 2),
    };

    [ObservableProperty]
    private int _notificationEnabledIndex;

    [ObservableProperty]
    private int _repeatIntervalIndex;

    [ObservableProperty]
    private int _recoveryEnabledIndex;

    /// <summary>当前选中 Provider 的凭据选项（来自注册表，不写死在 XAML）。</summary>
    public IReadOnlyList<ProviderCredentialOption> CredentialOptions { get; private set; } =
        Array.Empty<ProviderCredentialOption>();

    public bool ShowCredentialModeSelector => CredentialOptions.Count > 1;

    /// <summary>RadioButtons 与凭据模式的互相映射（凭据选项来自注册表）。</summary>
    public int CredentialModeIndex
    {
        get
        {
            int index = CredentialOptions.ToList()
                .FindIndex(o => string.Equals(o.CredentialTypeId, SelectedCredentialMode, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? 0 : index;
        }
        set
        {
            if (value >= 0 && value < CredentialOptions.Count)
            {
                SelectedCredentialMode = CredentialOptions[value].CredentialTypeId;
            }
        }
    }

    /// <summary>当前 Provider 描述与密钥输入说明。</summary>
    public string ProviderDescription { get; private set; } = string.Empty;

    public string ApiKeyInputHint { get; private set; } = string.Empty;

    public bool IsEditing => _context.AccountId is not null;

    public bool HasStoredCredential => _context.HasStoredCredential;

    public string Title => IsEditing ? "编辑账户" : "添加账户";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanTest))]
    private string _selectedProviderId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _selectedCredentialMode = string.Empty;

    /// <summary>编辑时保存的原始凭据模式（用于“更改模式必须重新测试连接”）。</summary>
    private readonly string? _effectiveOriginalMode;

    /// <summary>凭据模式更改后必须重新测试连接才能保存。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _modeChangedRequiresRetest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _displayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanTest))]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanTest))]
    private bool _isTesting;

    [ObservableProperty]
    private bool _hasTestResult;

    [ObservableProperty]
    private StatusSeverity _testSeverity = StatusSeverity.Informational;

    [ObservableProperty]
    private string _testTitle = string.Empty;

    [ObservableProperty]
    private string _testResultText = string.Empty;

    [ObservableProperty]
    private bool _hasValidationMessage;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _autoRefreshEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private int _refreshIntervalMinutes = MonitoringIntervals.DefaultMinutes;

    public ObservableCollection<ThresholdEditorItem> ThresholdItems { get; } = new();

    /// <summary>尚无任何币种余额时显示提示，避免阈值区空白。</summary>
    public bool ShowThresholdEmptyHint => ThresholdItems.Count == 0;

    public bool ThresholdsValid => ThresholdItems.All(i =>
        string.IsNullOrWhiteSpace(i.ThresholdText) || i.TryParseAmount(out _));

    public bool CanSave =>
        !IsTesting
        && !ModeChangedRequiresRetest
        && !string.IsNullOrWhiteSpace(DisplayName)
        && (!string.IsNullOrWhiteSpace(ApiKey) || (IsEditing && HasStoredCredential))
        && ThresholdsValid
        && MonitoringIntervals.Options.Contains(RefreshIntervalMinutes);

    public bool CanTest =>
        !IsTesting
        && (!string.IsNullOrWhiteSpace(ApiKey) || (IsEditing && HasStoredCredential));

    public AsyncRelayCommand TestCommand { get; }

    public AccountEditorViewModel(IAccountManager accountManager, AccountEditorContext context)
    {
        _accountManager = accountManager;
        _context = context;

        // Initialize commands before assigning observable properties: the generated
        // property setters raise On*Changed hooks that notify the TestCommand.
        TestCommand = new AsyncRelayCommand(TestConnectionAsync, () => CanTest);

        SelectedProviderId = context.InitialProviderId;
        DisplayName = context.InitialDisplayName ?? string.Empty;
        ApiKey = string.Empty;
        TestSeverity = StatusSeverity.Informational;
        TestTitle = string.Empty;
        TestResultText = string.Empty;
        ValidationMessage = string.Empty;

        AutoRefreshEnabled = context.InitialMonitoring.AutoRefreshEnabled;
        RefreshIntervalMinutes = context.InitialMonitoring.RefreshIntervalMinutes;
        NotificationEnabledIndex = EnabledIndex(context.InitialNotification.NotificationsEnabled);
        RepeatIntervalIndex = RepeatIndex(context.InitialNotification.RepeatIntervalHours);
        RecoveryEnabledIndex = EnabledIndex(context.InitialNotification.RecoveryNotificationsEnabled);

        ApplyProviderCapabilities(context.InitialProviderId);
        SelectedCredentialMode = context.CredentialMode ?? ProviderInfoFor(context.InitialProviderId)?.DefaultCredentialOption.CredentialTypeId ?? string.Empty;
        // 旧账户未保存凭据模式时按该 Provider 默认模式视为“未更改”，避免升级后要求重测。
        _effectiveOriginalMode = IsEditing
            ? context.CredentialMode
                ?? ProviderInfoFor(context.InitialProviderId)?.DefaultCredentialOption.CredentialTypeId
            : null;

        var rules = context.InitialMonitoring.Thresholds;
        foreach (var metric in context.CurrentMetrics)
        {
            var rule = rules.FirstOrDefault(r => r.MetricId == metric.MetricId);
            var item = new ThresholdEditorItem(metric, rule);
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanSave));
            ThresholdItems.Add(item);
        }

        ThresholdItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowThresholdEmptyHint));
            OnPropertyChanged(nameof(CanSave));
        };
    }

    partial void OnDisplayNameChanged(string value) =>
        TestCommand.NotifyCanExecuteChanged();

    partial void OnApiKeyChanged(string value) =>
        TestCommand.NotifyCanExecuteChanged();

    partial void OnIsTestingChanged(bool value) =>
        TestCommand.NotifyCanExecuteChanged();

    partial void OnSelectedProviderIdChanged(string value)
    {
        ApplyProviderCapabilities(value);
        // 切换 Provider 时恢复该 Provider 的默认凭据模式（编辑中切换 Provider 视为新配置）。
        SelectedCredentialMode = ProviderInfoFor(value)?.DefaultCredentialOption.CredentialTypeId ?? string.Empty;
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanTest));
    }

    partial void OnSelectedCredentialModeChanged(string value)
    {
        OnPropertyChanged(nameof(CredentialModeIndex));
        if (IsEditing
            && _effectiveOriginalMode is not null
            && !string.Equals(value, _effectiveOriginalMode, StringComparison.OrdinalIgnoreCase))
        {
            // 更改模式要求重新测试连接：清除旧测试结果并提示。
            ModeChangedRequiresRetest = true;
            HasTestResult = false;
            HasValidationMessage = false;
        }
        else
        {
            ModeChangedRequiresRetest = false;
        }
    }

    public void SetApiKey(string password) => ApiKey = password;

    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        IsTesting = true;
        HasTestResult = false;
        HasValidationMessage = false;
        try
        {
            var result = await _accountManager.TestConnectionAsync(
                SelectedProviderId,
                SelectedCredentialMode,
                string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
                _context.AccountId,
                cancellationToken);

            HasTestResult = true;
            if (result.IsSuccess)
            {
                ModeChangedRequiresRetest = false;
            }
            if (result.IsSuccess && result.Snapshot is { } snapshot)
            {
                TestSeverity = StatusSeverity.Success;
                TestTitle = "连接成功";
                TestResultText = snapshot.Metrics.Count == 0
                    ? "连接成功，但接口未返回余额明细。点击保存后才会写入账户与凭据。"
                    : string.Join(
                        "；",
                        snapshot.Metrics.Select(b =>
                            $"{BalanceMetricText.ValueText(b)}"))
                        + "。点击保存后才会写入账户与凭据。";

                // 把接口返回的指标余额同步到阈值区，让添加流程也能直接配置阈值。
                ApplyTestMetrics(snapshot.Metrics);
            }
            else
            {
                TestSeverity = StatusSeverity.Error;
                TestTitle = "连接失败";
                TestResultText = result.Error?.Message ?? "测试连接失败。";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            HasTestResult = true;
            TestSeverity = StatusSeverity.Error;
            TestTitle = "连接失败";
            TestResultText = "测试连接时发生意外错误，请稍后重试。";
        }
        finally
        {
            IsTesting = false;
        }
    }

    public bool TryBuildResult(out AccountEditorResult? result)
    {
        result = null;

        if (IsTesting)
        {
            ShowValidation("测试尚未完成，请稍候。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            ShowValidation("请输入账户显示名称。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(ApiKey) && !(IsEditing && HasStoredCredential))
        {
            ShowValidation("请输入 API Key。");
            return false;
        }

        if (!ThresholdsValid)
        {
            ShowValidation("阈值金额必须是不小于 0 的有效数字。");
            return false;
        }

        if (!MonitoringIntervals.Options.Contains(RefreshIntervalMinutes))
        {
            ShowValidation("刷新间隔无效。");
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        result = new AccountEditorResult
        {
            SaveRequested = true,
            ProviderId = SelectedProviderId,
            DisplayName = DisplayName.Trim(),
            ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
            CredentialMode = SelectedCredentialMode,
            Monitoring = new MonitoringSettings
            {
                AutoRefreshEnabled = AutoRefreshEnabled,
                RefreshIntervalMinutes = RefreshIntervalMinutes,
                Thresholds = ThresholdItems
                    .Select(i => i.BuildRule(now))
                    .Where(r => r is not null)
                    .Cast<BalanceThresholdRule>()
                    .ToList(),
            },
            Notification = new AccountNotificationSettings
            {
                NotificationsEnabled = EnabledFromIndex(NotificationEnabledIndex),
                RepeatIntervalHours = RepeatFromIndex(RepeatIntervalIndex),
                RecoveryNotificationsEnabled = EnabledFromIndex(RecoveryEnabledIndex),
            },
        };
        return true;
    }

    private static int EnabledIndex(bool? value) => value switch
    {
        null => 0,
        true => 1,
        false => 2,
    };

    private static bool? EnabledFromIndex(int index) => index switch
    {
        1 => true,
        2 => false,
        _ => null,
    };

    private static int RepeatIndex(int? hours) => hours switch
    {
        null => 0,
        0 => 1,
        6 => 2,
        12 => 3,
        24 => 4,
        72 => 5,
        _ => 0,
    };

    private static int? RepeatFromIndex(int index) => index switch
    {
        1 => 0,
        2 => 6,
        3 => 12,
        4 => 24,
        5 => 72,
        _ => null,
    };

    private void ApplyTestMetrics(IReadOnlyList<BalanceMetric> metrics)
    {
        var rules = _context.InitialMonitoring.Thresholds;
        foreach (var metric in metrics)
        {
            if (!metric.IsThresholdSupported)
            {
                continue;
            }

            var existing = ThresholdItems.FirstOrDefault(i => i.MetricId == metric.MetricId);
            if (existing is not null)
            {
                existing.CurrentAmount = BalanceMetricText.MainAmount(metric);
                continue;
            }

            var rule = rules.FirstOrDefault(r => r.MetricId == metric.MetricId);
            var item = new ThresholdEditorItem(metric, rule);
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanSave));
            ThresholdItems.Add(item);
        }
    }

    private void ShowValidation(string message)
    {
        ValidationMessage = message;
        HasValidationMessage = true;
    }

    private void ApplyProviderCapabilities(string providerId)
    {
        var info = ProviderInfoFor(providerId);
        CredentialOptions = info?.CredentialOptions ?? Array.Empty<ProviderCredentialOption>();
        ProviderDescription = info?.Description ?? string.Empty;
        ApiKeyInputHint = info?.ApiKeyInputHint ?? string.Empty;
        OnPropertyChanged(nameof(CredentialOptions));
        OnPropertyChanged(nameof(ShowCredentialModeSelector));
        OnPropertyChanged(nameof(CredentialModeIndex));
        OnPropertyChanged(nameof(ProviderDescription));
        OnPropertyChanged(nameof(ApiKeyInputHint));
    }

    private ProviderInfo? ProviderInfoFor(string providerId) =>
        _context.Providers.FirstOrDefault(p =>
            string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
}
