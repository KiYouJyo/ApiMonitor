using ApiBalanceMonitor.Helpers;
using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Providers;
using ApiBalanceMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiBalanceMonitor.ViewModels;

/// <summary>添加/编辑账户对话框的 ViewModel（测试连接只预览结果，保存才写入）。</summary>
public sealed partial class AccountEditorViewModel : ObservableObject
{
    private readonly IAccountManager _accountManager;
    private readonly AccountEditorContext _context;

    public IReadOnlyList<ProviderInfo> Providers => _context.Providers;

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

    public bool CanSave =>
        !IsTesting
        && !string.IsNullOrWhiteSpace(DisplayName)
        && (!string.IsNullOrWhiteSpace(ApiKey) || (IsEditing && HasStoredCredential));

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
                TestResultText = snapshot.Balances.Count == 0
                    ? "连接成功，但接口未返回余额明细。点击保存后才会写入账户与凭据。"
                    : string.Join(
                        "；",
                        snapshot.Balances.Select(b =>
                            $"{b.Currency} 总余额 {BalanceFormatter.Format(b.TotalBalance)}"))
                        + "。点击保存后才会写入账户与凭据。";
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

        result = new AccountEditorResult
        {
            SaveRequested = true,
            ProviderId = SelectedProviderId,
            DisplayName = DisplayName.Trim(),
            ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
        };
        return true;
    }

    private void ShowValidation(string message)
    {
        ValidationMessage = message;
        HasValidationMessage = true;
    }
}
