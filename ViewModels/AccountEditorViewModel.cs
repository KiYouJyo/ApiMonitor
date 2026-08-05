using System.Collections.ObjectModel;
using ApiMonitor.Helpers;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>
/// Provider 需要的非敏感配置字段编辑项（如 xAI Team ID）。
/// 标签/提示/占位符来自 ProviderInfo 声明的 RESW 键，不在 XAML 写死。
/// v0.9.0：支持布尔字段（自托管“允许 HTTP”等显式确认）。
/// </summary>
public sealed partial class ProviderConfigFieldItem : ObservableObject
{
    public string FieldId { get; }

    public string Label { get; }

    public string Hint { get; }

    public bool IsRequired { get; }

    public string Placeholder { get; }

    public bool IsBoolean { get; }

    public string OnContentText => L10n.Get("Settings.On");

    public string OffContentText => L10n.Get("Settings.Off");

    [ObservableProperty]
    private string _value = string.Empty;

    public ProviderConfigFieldItem(ProviderConfigField field, string? initialValue)
    {
        FieldId = field.FieldId;
        Label = L10n.Get(field.LabelKey);
        Hint = L10n.Get(field.HintKey);
        IsRequired = field.IsRequired;
        IsBoolean = field.Kind == ProviderConfigFieldKind.Boolean;
        Placeholder = string.IsNullOrWhiteSpace(field.PlaceholderKey)
            ? string.Empty
            : L10n.Get(field.PlaceholderKey);
        Value = initialValue ?? string.Empty;
    }

    public bool BooleanValue
    {
        get => bool.TryParse(Value, out bool value) && value;
        set
        {
            Value = value ? "true" : "false";
            OnPropertyChanged();
        }
    }
}

/// <summary>
/// 多槽位凭据输入项（v0.9.0）：Key+SK、Basic 用户名+密码、Bearer/QueryToken。
/// 支持按配置字段条件显示（如 OGC 仅 authMode=basic 时显示用户名/密码）。
/// </summary>
public sealed partial class CredentialSlotItem : ObservableObject
{
    private readonly ProviderCredentialSlot _slot;
    private readonly IReadOnlyDictionary<string, string> _configValues;

    public string SlotId { get; }

    public string Label { get; }

    public string Hint { get; }

    public bool IsRequired { get; }

    public bool IsSecret { get; }

    public string Placeholder { get; }

    public string OnContentText => L10n.Get("Settings.On");

    public string OffContentText => L10n.Get("Settings.Off");

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _isVisible = true;

    public CredentialSlotItem(
        ProviderCredentialSlot slot,
        string? initialValue,
        IReadOnlyDictionary<string, string> configValues)
    {
        _slot = slot;
        _configValues = configValues;
        SlotId = slot.SlotId;
        Label = L10n.Get(slot.LabelKey);
        Hint = L10n.Get(slot.HintKey);
        IsRequired = slot.IsRequired;
        IsSecret = slot.IsSecret;
        Placeholder = string.IsNullOrWhiteSpace(slot.PlaceholderKey)
            ? string.Empty
            : L10n.Get(slot.PlaceholderKey);
        Value = initialValue ?? string.Empty;
        RefreshVisibility(_configValues);
    }

    /// <summary>按当前配置字段值刷新条件显示（如 authMode 切换后）。</summary>
    public void RefreshVisibility(IReadOnlyDictionary<string, string> configValues)
    {
        if (string.IsNullOrWhiteSpace(_slot.ConditionalOnConfigFieldId))
        {
            IsVisible = true;
            return;
        }

        string? current = configValues.TryGetValue(_slot.ConditionalOnConfigFieldId, out var raw)
            ? raw?.Trim().ToLowerInvariant()
            : null;
        IsVisible = string.Equals(
            current,
            _slot.ConditionalOnConfigValue,
            StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>添加/编辑账户对话框的 ViewModel（测试连接只预览结果，保存才写入）。</summary>
public sealed partial class AccountEditorViewModel : ObservableObject
{
    private readonly IAccountManager _accountManager;
    private readonly AccountEditorContext _context;

    public IReadOnlyList<ProviderInfo> Providers => _context.Providers;

    public IReadOnlyList<int> RefreshIntervals =>
        MonitoringIntervals.OptionsFor(SelectedProviderCategory);

    /// <summary>通知开关选项（0=继承全局，1=开启，2=关闭）。</summary>
    public sealed record NotificationPreferenceOption(string Label, int Value);

    /// <summary>重复提醒间隔选项（-1=继承全局，其余为小时数）。</summary>
    public sealed record NotificationRepeatOption(string Label, int Value);

    public IReadOnlyList<NotificationPreferenceOption> NotificationEnabledOptions { get; } = new[]
    {
        new NotificationPreferenceOption(L10n.Get("Notification.InheritGlobal"), 0),
        new NotificationPreferenceOption(L10n.Get("Settings.On"), 1),
        new NotificationPreferenceOption(L10n.Get("Settings.Off"), 2),
    };

    public IReadOnlyList<NotificationRepeatOption> NotificationRepeatOptions { get; } = new[]
    {
        new NotificationRepeatOption(L10n.Get("Notification.InheritGlobal"), -1),
        new NotificationRepeatOption(L10n.Get("Notification.NoRepeat"), 0),
        new NotificationRepeatOption(L10n.Get("Notification.Hours6"), 6),
        new NotificationRepeatOption(L10n.Get("Notification.Hours12"), 12),
        new NotificationRepeatOption(L10n.Get("Notification.Hours24"), 24),
        new NotificationRepeatOption(L10n.Get("Notification.Days3"), 72),
    };

    public IReadOnlyList<NotificationPreferenceOption> RecoveryOptions { get; } = new[]
    {
        new NotificationPreferenceOption(L10n.Get("Notification.InheritGlobal"), 0),
        new NotificationPreferenceOption(L10n.Get("Settings.On"), 1),
        new NotificationPreferenceOption(L10n.Get("Settings.Off"), 2),
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

    /// <summary>当前 Provider 需要的非敏感配置字段（动态，来自注册表）。</summary>
    public ObservableCollection<ProviderConfigFieldItem> ConfigFieldItems { get; } = new();

    public bool ShowConfigFields => ConfigFieldItems.Count > 0;

    /// <summary>当前 Provider 需要的凭据槽位输入项（动态，来自注册表）。</summary>
    public ObservableCollection<CredentialSlotItem> CredentialSlotItems { get; } = new();

    public bool ShowCredentialSlots => CredentialSlotItems.Count > 0;

    /// <summary>是否显示“本次探测可能消耗一次 API 调用”提示。</summary>
    public bool ShowProbeConsumesQuotaHint =>
        SelectedProviderInfo?.EffectiveProbeConsumesQuota == true;

    public ProviderCategory SelectedProviderCategory =>
        SelectedProviderInfo?.EffectiveCategory ?? ProviderCategory.ArtificialIntelligence;

    private ProviderInfo? SelectedProviderInfo =>
        ProviderInfoFor(SelectedProviderId);

    public bool IsEditing => _context.AccountId is not null;

    public bool HasStoredCredential => _context.HasStoredCredential;

    /// <summary>编辑中切换了 Provider：禁止沿用旧凭据，必须重新录入并测试。</summary>
    public bool ProviderChanged => _providerChanged;

    public bool ShowKeepCredentialHint => IsEditing && HasStoredCredential && !_providerChanged;

    public bool ShowMissingCredentialHint => IsEditing && !HasStoredCredential;

    public string Title => IsEditing ? L10n.Get("Dialog.EditAccountTitle") : L10n.Get("Dialog.AddAccountTitle");

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

    /// <summary>编辑中切换 Provider 后必须重新录入密钥并测试连接才能保存。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _providerChangedRequiresRetest;

    /// <summary>Provider 切换时通知视图清空密码框（避免残留上一供应商的敏感输入）。</summary>
    public event Action? ApiKeyCleared;

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

    public bool RequiredConfigFieldsFilled => ConfigFieldItems
        .Where(i => i.IsRequired)
        .All(i => !string.IsNullOrWhiteSpace(i.Value.Trim()));

    private bool _providerChanged;

    public bool CanSave =>
        !IsTesting
        && !ModeChangedRequiresRetest
        && !ProviderChangedRequiresRetest
        && !string.IsNullOrWhiteSpace(DisplayName)
        && VisibleRequiredSlotsSatisfied
        && RequiredConfigFieldsFilled
        && ThresholdsValid
        && MonitoringIntervals.OptionsFor(SelectedProviderCategory).Contains(RefreshIntervalMinutes);

    public bool CanTest =>
        !IsTesting
        && VisibleRequiredSlotsSatisfied
        && RequiredConfigFieldsFilled;

    private bool KeepExistingCredentialAllowed => IsEditing && HasStoredCredential && !_providerChanged;

    /// <summary>
    /// 所有当前可见且必填的凭据槽位都已填写，或编辑中沿用已有凭据。
    /// </summary>
    private bool VisibleRequiredSlotsSatisfied
    {
        get
        {
            foreach (var item in CredentialSlotItems)
            {
                if (!item.IsVisible || !item.IsRequired)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(item.Value))
                {
                    continue;
                }

                bool kept = IsEditing
                    && !_providerChanged
                    && (string.Equals(item.SlotId, CredentialSlots.Primary, StringComparison.Ordinal)
                        ? HasStoredCredential
                            || (_context.CredentialSlots.TryGetValue(item.SlotId, out bool primaryPresent)
                                && primaryPresent)
                        : _context.CredentialSlots.TryGetValue(item.SlotId, out bool present)
                            && present);
                if (!kept)
                {
                    return false;
                }
            }

            return true;
        }
    }

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
        // v0.9.0：新地图账户默认关闭自动刷新（启用后 6 小时、最短 1 小时）。
        if (!IsEditing && SelectedProviderCategory == ProviderCategory.Geospatial)
        {
            AutoRefreshEnabled = false;
            RefreshIntervalMinutes = MonitoringIntervals.GeospatialDefaultMinutes;
        }

        // v0.9.0：新地图/GIS 服务账户默认关闭健康通知（用户必须主动开启）。
        if (!IsEditing && SelectedProviderCategory != ProviderCategory.ArtificialIntelligence)
        {
            NotificationEnabledIndex = 2;
        }
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

        ConfigFieldItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowConfigFields));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanTest));
            RefreshCredentialSlotVisibility();
        };

        CredentialSlotItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowCredentialSlots));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanTest));
        };
    }

    partial void OnDisplayNameChanged(string value) =>
        TestCommand.NotifyCanExecuteChanged();

    partial void OnApiKeyChanged(string value)
    {
        TestCommand.NotifyCanExecuteChanged();
        // 旧单密码框入口同步到 primary 槽位（多槽位模型）。
        var primary = CredentialSlotItems.FirstOrDefault(i =>
            string.Equals(i.SlotId, CredentialSlots.Primary, StringComparison.OrdinalIgnoreCase));
        if (primary is not null && !string.Equals(primary.Value, value, StringComparison.Ordinal))
        {
            primary.Value = value;
        }
    }

    partial void OnIsTestingChanged(bool value) =>
        TestCommand.NotifyCanExecuteChanged();

    partial void OnSelectedProviderIdChanged(string value)
    {
        ApplyProviderCapabilities(value);
        // 编辑中切换 Provider：不得沿用上一 Provider 的密钥，清空敏感输入并要求重测。
        _providerChanged = IsEditing
            && !string.Equals(value, _context.InitialProviderId, StringComparison.OrdinalIgnoreCase);
        // 切换 Provider 时恢复该 Provider 的默认凭据模式（编辑中切换 Provider 视为新配置）。
        SelectedCredentialMode = ProviderInfoFor(value)?.DefaultCredentialOption.CredentialTypeId ?? string.Empty;
        if (_providerChanged)
        {
            SetApiKey(string.Empty);
            ApiKeyCleared?.Invoke();
            HasTestResult = false;
            HasValidationMessage = false;
            ProviderChangedRequiresRetest = true;
        }
        else
        {
            ProviderChangedRequiresRetest = false;
        }

        RebuildConfigFields();
        RebuildCredentialSlots();
        OnPropertyChanged(nameof(RefreshIntervals));
        OnPropertyChanged(nameof(SelectedProviderCategory));
        OnPropertyChanged(nameof(ShowProbeConsumesQuotaHint));
        OnPropertyChanged(nameof(ShowKeepCredentialHint));
        OnPropertyChanged(nameof(ShowMissingCredentialHint));
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

    /// <summary>v0.9.0：把指定槽位设置为输入值（旧单密码框入口保持可用）。</summary>
    public void SetCredentialSlotValue(string slotId, string value)
    {
        var item = CredentialSlotItems.FirstOrDefault(i =>
            string.Equals(i.SlotId, slotId, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            item.Value = value;
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanTest));
        }

        if (string.Equals(slotId, CredentialSlots.Primary, StringComparison.OrdinalIgnoreCase))
        {
            ApiKey = value;
        }
    }

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
                cancellationToken,
                BuildProviderConfig(),
                BuildCredentialSlots());

            HasTestResult = true;
            if (result.IsSuccess)
            {
                ModeChangedRequiresRetest = false;
                ProviderChangedRequiresRetest = false;
            }
            if (result.IsSuccess && result.Snapshot is { } snapshot)
            {
                TestSeverity = StatusSeverity.Success;
                TestTitle = L10n.Get("Dialog.TestSuccessTitle");
                TestResultText = snapshot.Metrics.Count == 0
                    ? L10n.Get("Dialog.TestSuccessNoBalance")
                    : string.Join(
                        "；",
                        snapshot.Metrics.Select(b =>
                            $"{BalanceMetricText.ValueText(b)}"))
                        + L10n.Get("Dialog.TestSuccessSuffix");

                // 把接口返回的指标余额同步到阈值区，让添加流程也能直接配置阈值。
                ApplyTestMetrics(snapshot.Metrics);
            }
            else
            {
                TestSeverity = StatusSeverity.Error;
                TestTitle = L10n.Get("Dialog.TestFailedTitle");
                TestResultText = result.Error?.Message ?? L10n.Get("Dialog.TestFailedDefault");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            HasTestResult = true;
            TestSeverity = StatusSeverity.Error;
            TestTitle = L10n.Get("Dialog.TestFailedTitle");
            TestResultText = L10n.Get("Dialog.TestUnexpectedError");
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
            ShowValidation(L10n.Get("Dialog.ValidationTestPending"));
            return false;
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            ShowValidation(L10n.Get("Dialog.ValidationNameRequired"));
            return false;
        }

        if (string.IsNullOrWhiteSpace(ApiKey) && !KeepExistingCredentialAllowed)
        {
            // 保持旧校验：primary 槽位未填写且不能沿用时不保存。
            if (CredentialSlotItems.Count > 0
                && CredentialSlotItems.Any(i =>
                    string.Equals(i.SlotId, CredentialSlots.Primary, StringComparison.OrdinalIgnoreCase)))
            {
                ShowValidation(L10n.Get("Dialog.ValidationKeyRequired"));
                return false;
            }

            if (CredentialSlotItems.Count == 0)
            {
                ShowValidation(L10n.Get("Dialog.ValidationKeyRequired"));
                return false;
            }
        }

        if (!VisibleRequiredSlotsSatisfied)
        {
            ShowValidation(L10n.Get("Dialog.ValidationKeyRequired"));
            return false;
        }

        if (!RequiredConfigFieldsFilled)
        {
            ShowValidation(L10n.Get("Dialog.ValidationConfigRequired"));
            return false;
        }

        if (!ThresholdsValid)
        {
            ShowValidation(L10n.Get("Dialog.ValidationThresholdInvalid"));
            return false;
        }

        if (!MonitoringIntervals.OptionsFor(SelectedProviderCategory).Contains(RefreshIntervalMinutes))
        {
            ShowValidation(L10n.Get("Dialog.ValidationIntervalInvalid"));
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
            ProviderConfig = BuildProviderConfig(),
            CredentialSlots = BuildCredentialSlots(),
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

    private void RebuildConfigFields()
    {
        foreach (var item in ConfigFieldItems)
        {
            item.PropertyChanged -= OnConfigFieldChanged;
        }

        ConfigFieldItems.Clear();

        var info = ProviderInfoFor(SelectedProviderId);
        if (info is null)
        {
            return;
        }

        foreach (var field in info.EffectiveConfigFields)
        {
            string? existing = null;
            if (IsEditing && !_providerChanged)
            {
                _context.ProviderConfig.TryGetValue(field.FieldId, out existing);
            }

            var item = new ProviderConfigFieldItem(field, existing);
            item.PropertyChanged += OnConfigFieldChanged;
            ConfigFieldItems.Add(item);
        }
    }

    private void RebuildCredentialSlots()
    {
        foreach (var item in CredentialSlotItems)
        {
            item.PropertyChanged -= OnCredentialSlotChanged;
        }

        CredentialSlotItems.Clear();

        var info = ProviderInfoFor(SelectedProviderId);
        if (info is null)
        {
            return;
        }

        foreach (var slot in info.EffectiveCredentialSlots)
        {
            // 编辑中绝不回显已存凭据值（只显示“已保存”提示）；新增时为空输入。
            var item = new CredentialSlotItem(slot, initialValue: null, BuildConfigValueMap());
            item.PropertyChanged += OnCredentialSlotChanged;
            CredentialSlotItems.Add(item);
        }

        // 同步旧单密码框入口（primary 槽位）。
        ApiKey = CredentialSlotItems.FirstOrDefault(i =>
            string.Equals(i.SlotId, CredentialSlots.Primary, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
    }

    private void RefreshCredentialSlotVisibility()
    {
        var config = BuildConfigValueMap();
        foreach (var item in CredentialSlotItems)
        {
            item.RefreshVisibility(config);
        }

        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanTest));
    }

    private IReadOnlyDictionary<string, string> BuildConfigValueMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in ConfigFieldItems)
        {
            map[field.FieldId] = field.Value.Trim();
        }

        return map;
    }

    private void OnCredentialSlotChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is CredentialSlotItem item
            && string.Equals(item.SlotId, CredentialSlots.Primary, StringComparison.OrdinalIgnoreCase))
        {
            ApiKey = item.Value;
        }

        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanTest));
    }

    private IReadOnlyDictionary<string, string> BuildCredentialSlots() =>
        CredentialSlotItems
            .Where(i => i.IsVisible && !string.IsNullOrWhiteSpace(i.Value))
            .ToDictionary(i => i.SlotId, i => i.Value.Trim(), StringComparer.Ordinal);

    private void OnConfigFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RefreshCredentialSlotVisibility();
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanTest));
    }

    private IReadOnlyDictionary<string, string> BuildProviderConfig() =>
        ConfigFieldItems
            .Where(i => !string.IsNullOrWhiteSpace(i.Value.Trim()))
            .ToDictionary(i => i.FieldId, i => i.Value.Trim(), StringComparer.Ordinal);

    private ProviderInfo? ProviderInfoFor(string providerId) =>
        _context.Providers.FirstOrDefault(p =>
            string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
}
