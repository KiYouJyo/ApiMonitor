using ApiBalanceMonitor.Providers;
using ApiBalanceMonitor.ViewModels;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 清晰的组合根：集中创建服务、Provider 注册表与 ViewModel，
/// 不使用静态全局状态保存账户或密钥。
/// </summary>
public sealed class CompositionRoot
{
    public AppLog Log { get; }

    public MainViewModel MainViewModel { get; }

    public DialogService DialogService { get; }

    public CompositionRoot()
    {
        string dataDirectory = AppPaths.GetLocalDataDirectory();
        Directory.CreateDirectory(dataDirectory);

        Log = new AppLog(dataDirectory);

        var http = new HttpRequestService(TimeSpan.FromSeconds(15));
        var deepSeek = new DeepSeekBalanceProvider(http, Log);
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { deepSeek });

        var secretStore = new CredentialLockerSecretStore(Log);
        var accountStore = new JsonAccountStore(dataDirectory);
        var snapshotStore = new JsonBalanceSnapshotStore(dataDirectory);

        var accountManager = new AccountManager(
            accountStore,
            snapshotStore,
            secretStore,
            registry,
            Log);

        DialogService = new DialogService(accountManager);
        MainViewModel = new MainViewModel(accountManager, DialogService, Log);
    }
}
