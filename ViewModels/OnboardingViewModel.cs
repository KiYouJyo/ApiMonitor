using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>引导第二步 Provider 说明行（非敏感）。</summary>
public sealed record OnboardingProviderRow(string ProviderId, string DisplayName, string Description);

/// <summary>引导第三步运行方式选项（只展示默认状态，不自动开启任何功能）。</summary>
public sealed record OnboardingRunOption(string Title, string Description, string DefaultState);

/// <summary>引导第四步功能入口。</summary>
public sealed record OnboardingEntry(string Title, string Description);

/// <summary>
/// v1.0.0：首次启动引导（四步）。
/// 只在当前身份首次启动（OnboardingCompleted=false）时显示；完成/跳过后不再
/// 自动弹出；设置页可重置重新打开；进度中断可安全恢复（步数保存在 VM）。
/// 引导不记录 API Key；不自动开启通知、登录启动、自动刷新或关闭到托盘。
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly IOnboardingStateService _state;
    private readonly Func<Task> _completeCallback;
    private readonly Func<Task> _addAccountCallback;
    private readonly Action _openSettingsCallback;
    private readonly Func<Uri, Task<bool>>? _launchUri;

    [ObservableProperty]
    private int _currentStep;

    public int StepCount => 4;

    public bool IsFirstStep => CurrentStep == 0;

    public bool IsLastStep => CurrentStep == StepCount - 1;

    public bool ShowBackButton => CurrentStep > 0;

    public bool ShowNextButton => CurrentStep < StepCount - 1;

    public bool ShowFinishButton => CurrentStep == StepCount - 1;

    public string StepTitleText => L10n.Get($"Onboarding.Step{CurrentStep + 1}.Title");

    public string StepBodyText => L10n.Get($"Onboarding.Step{CurrentStep + 1}.Body");

    public string StepProgressText => L10n.Format("Onboarding.StepProgress", CurrentStep + 1, StepCount);

    /// <summary>第一步：隐私要点。</summary>
    public IReadOnlyList<string> PrivacyHighlights { get; } = new[]
    {
        L10n.Get("Onboarding.Privacy.LocalPurpose"),
        L10n.Get("Onboarding.Privacy.LocalStorage"),
        L10n.Get("Onboarding.Privacy.CredentialLocker"),
        L10n.Get("Onboarding.Privacy.ProviderOnly"),
        L10n.Get("Onboarding.Privacy.NoCloud"),
        L10n.Get("Onboarding.Privacy.NoTelemetry"),
    };

    /// <summary>第二步：可添加的 Provider（DeepSeek / OpenRouter 普通 Key / Management Key）。</summary>
    public IReadOnlyList<OnboardingProviderRow> Providers { get; } = new[]
    {
        new OnboardingProviderRow(
            "deepseek",
            L10n.Get("Onboarding.Provider.DeepSeek.Title"),
            L10n.Get("Onboarding.Provider.DeepSeek.Description")),
        new OnboardingProviderRow(
            "openrouter",
            L10n.Get("Onboarding.Provider.OpenRouterApiKey.Title"),
            L10n.Get("Onboarding.Provider.OpenRouterApiKey.Description")),
        new OnboardingProviderRow(
            "openrouter-management",
            L10n.Get("Onboarding.Provider.OpenRouterManagementKey.Title"),
            L10n.Get("Onboarding.Provider.OpenRouterManagementKey.Description")),
    };

    /// <summary>第三步：运行方式选项（默认保持安全默认值，不自动开启）。</summary>
    public IReadOnlyList<OnboardingRunOption> RunOptions { get; } = new[]
    {
        new OnboardingRunOption(
            L10n.Get("Onboarding.Run.AutoRefresh.Title"),
            L10n.Get("Onboarding.Run.AutoRefresh.Description"),
            L10n.Get("Onboarding.Run.DefaultOff")),
        new OnboardingRunOption(
            L10n.Get("Onboarding.Run.BalanceAlerts.Title"),
            L10n.Get("Onboarding.Run.BalanceAlerts.Description"),
            L10n.Get("Onboarding.Run.DefaultOff")),
        new OnboardingRunOption(
            L10n.Get("Onboarding.Run.CloseToTray.Title"),
            L10n.Get("Onboarding.Run.CloseToTray.Description"),
            L10n.Get("Onboarding.Run.DefaultOff")),
        new OnboardingRunOption(
            L10n.Get("Onboarding.Run.StartAtLogin.Title"),
            L10n.Get("Onboarding.Run.StartAtLogin.Description"),
            L10n.Get("Onboarding.Run.DefaultOff")),
    };

    /// <summary>第四步：主要功能入口。</summary>
    public IReadOnlyList<OnboardingEntry> Entries { get; } = new[]
    {
        new OnboardingEntry(
            L10n.Get("Onboarding.Entry.AddAccount.Title"),
            L10n.Get("Onboarding.Entry.AddAccount.Description")),
        new OnboardingEntry(
            L10n.Get("Onboarding.Entry.Tray.Title"),
            L10n.Get("Onboarding.Entry.Tray.Description")),
        new OnboardingEntry(
            L10n.Get("Onboarding.Entry.FloatingWindow.Title"),
            L10n.Get("Onboarding.Entry.FloatingWindow.Description")),
        new OnboardingEntry(
            L10n.Get("Onboarding.Entry.Insights.Title"),
            L10n.Get("Onboarding.Entry.Insights.Description")),
        new OnboardingEntry(
            L10n.Get("Onboarding.Entry.Settings.Title"),
            L10n.Get("Onboarding.Entry.Settings.Description")),
    };

    public IAsyncRelayCommand NextCommand { get; }

    public IAsyncRelayCommand BackCommand { get; }

    public IAsyncRelayCommand SkipCommand { get; }

    public IAsyncRelayCommand FinishCommand { get; }

    public IAsyncRelayCommand AddAccountCommand { get; }

    public IAsyncRelayCommand OpenSettingsCommand { get; }

    public IAsyncRelayCommand OpenPrivacyPolicyCommand { get; }

    public OnboardingViewModel(
        IOnboardingStateService state,
        Func<Task> completeCallback,
        Func<Task> addAccountCallback,
        Action openSettingsCallback,
        Func<Uri, Task<bool>>? launchUri = null)
    {
        _state = state;
        _completeCallback = completeCallback;
        _addAccountCallback = addAccountCallback;
        _openSettingsCallback = openSettingsCallback;
        _launchUri = launchUri;

        NextCommand = new AsyncRelayCommand(NextAsync, () => !IsLastStep);
        BackCommand = new AsyncRelayCommand(BackAsync, () => !IsFirstStep);
        SkipCommand = new AsyncRelayCommand(() => CompleteAsync(skipped: true));
        FinishCommand = new AsyncRelayCommand(() => CompleteAsync(skipped: false));
        AddAccountCommand = new AsyncRelayCommand(() => _addAccountCallback());
        OpenSettingsCommand = new AsyncRelayCommand(() =>
        {
            _openSettingsCallback();
            return Task.CompletedTask;
        });
        OpenPrivacyPolicyCommand = new AsyncRelayCommand(OpenPrivacyPolicyAsync);
    }

    private async Task OpenPrivacyPolicyAsync()
    {
        if (_launchUri is null
            || !Uri.TryCreate(
                "https://github.com/KiYouJyo/ApiMonitor/blob/main/PRIVACY.md",
                UriKind.Absolute,
                out var uri))
        {
            return;
        }

        await _launchUri(uri);
    }

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(ShowBackButton));
        OnPropertyChanged(nameof(ShowNextButton));
        OnPropertyChanged(nameof(ShowFinishButton));
        OnPropertyChanged(nameof(StepTitleText));
        OnPropertyChanged(nameof(StepBodyText));
        OnPropertyChanged(nameof(StepProgressText));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    private Task NextAsync()
    {
        if (CurrentStep < StepCount - 1)
        {
            CurrentStep++;
        }

        return Task.CompletedTask;
    }

    private Task BackAsync()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }

        return Task.CompletedTask;
    }

    private async Task CompleteAsync(bool skipped)
    {
        await _state.MarkCompletedAsync(skipped, CancellationToken.None);
        await _completeCallback();
    }

    /// <summary>从设置页重新打开引导：重置完成标记并回到第一步。</summary>
    public async Task ReopenAsync(CancellationToken cancellationToken)
    {
        await _state.ResetAsync(cancellationToken);
        CurrentStep = 0;
    }

    /// <summary>应用启动时检查：未完成则导航到引导页。</summary>
    public async Task<bool> IsCompletedAsync(CancellationToken cancellationToken) =>
        await _state.IsCompletedAsync(cancellationToken);
}
