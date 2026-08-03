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

    public bool IsEditing => _context.AccountId is not null;

    public bool HasStoredCredential => _context.HasStoredCredential;

    public string Title => IsEditing ? "编辑账户" : "添加账户";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanTest))]
    private string _selectedProviderId;

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
                string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
                _context.AccountId,
                cancellationToken);

            HasTestResult = true;
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
        };
        return true;
    }

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
}
