using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.Tests.TestHelpers;
using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v1.0.0：首次启动引导测试。
/// 全新安装显示引导；完成/跳过后不再弹出；设置页可重新打开；
/// 引导不自动开启通知/登录启动；中断可安全恢复；损坏文件可恢复。
/// </summary>
public sealed class OnboardingTests
{
    [Fact]
    public async Task FreshInstall_IsNotCompleted()
    {
        using var temp = new TempDirectory();
        var store = new JsonOnboardingStateStore(temp.Path);

        Assert.False(await store.IsCompletedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MarkCompleted_ThenIsCompleted_NoRepeat()
    {
        using var temp = new TempDirectory();
        var store = new JsonOnboardingStateStore(temp.Path);

        await store.MarkCompletedAsync(skipped: false, CancellationToken.None);

        Assert.True(await store.IsCompletedAsync(CancellationToken.None));
        var data = await store.LoadAsync(CancellationToken.None);
        Assert.True(data.OnboardingCompleted);
        Assert.False(data.OnboardingSkipped);
        Assert.NotNull(data.CompletedAtUtc);
    }

    [Fact]
    public async Task MarkCompleted_Skipped_StillNoRepeat()
    {
        using var temp = new TempDirectory();
        var store = new JsonOnboardingStateStore(temp.Path);

        await store.MarkCompletedAsync(skipped: true, CancellationToken.None);

        Assert.True(await store.IsCompletedAsync(CancellationToken.None));
        var data = await store.LoadAsync(CancellationToken.None);
        Assert.True(data.OnboardingSkipped);
    }

    [Fact]
    public async Task Reset_AllowsReopenFromSettings()
    {
        using var temp = new TempDirectory();
        var store = new JsonOnboardingStateStore(temp.Path);
        await store.MarkCompletedAsync(skipped: false, CancellationToken.None);

        await store.ResetAsync(CancellationToken.None);

        Assert.False(await store.IsCompletedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CorruptStateFile_IsBackedUp_AndDefaultsToNotCompleted()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, JsonOnboardingStateStore.FileName), "{ broken !!!");

        var store = new JsonOnboardingStateStore(temp.Path);

        Assert.False(await store.IsCompletedAsync(CancellationToken.None));
        Assert.Single(Directory.GetFiles(temp.Path, "*.corrupt-*.json"));
    }

    [Fact]
    public async Task StateFile_NeverContainsSecrets()
    {
        using var temp = new TempDirectory();
        var store = new JsonOnboardingStateStore(temp.Path);
        await store.MarkCompletedAsync(skipped: false, CancellationToken.None);

        string json = await File.ReadAllTextAsync(Path.Combine(temp.Path, JsonOnboardingStateStore.FileName));

        Assert.DoesNotContain("sk-", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api-key", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StateFile_DoesNotCreateOrReadAccountsFile()
    {
        using var temp = new TempDirectory();
        var store = new JsonOnboardingStateStore(temp.Path);
        await store.MarkCompletedAsync(skipped: false, CancellationToken.None);

        // Store 版首次启动按全新应用处理：引导状态文件绝不创建/读取 accounts.json。
        Assert.False(File.Exists(Path.Combine(temp.Path, JsonAccountStore.FileName)));
        Assert.False(File.Exists(Path.Combine(temp.Path, "accounts.json")));
    }

    [Fact]
    public async Task ViewModel_NextBack_StepsAndCommandsEnablement()
    {
        using var temp = new TempDirectory();
        var vm = CreateVm(temp.Path, out _);

        Assert.Equal(0, vm.CurrentStep);
        Assert.True(vm.ShowNextButton);
        Assert.False(vm.ShowFinishButton);
        Assert.False(vm.ShowBackButton);

        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.CurrentStep);
        Assert.True(vm.ShowBackButton);

        await vm.BackCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.CurrentStep);
    }

    [Fact]
    public async Task ViewModel_Finish_MarksCompletedAndCallsCallback()
    {
        using var temp = new TempDirectory();
        int completed = 0;
        var vm = CreateVm(temp.Path, out _, completed: () => completed++);

        await vm.FinishCommand.ExecuteAsync(null);

        Assert.Equal(1, completed);
        Assert.True(await vm.IsCompletedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ViewModel_Skip_MarksCompletedAndCallsCallback()
    {
        using var temp = new TempDirectory();
        int completed = 0;
        var vm = CreateVm(temp.Path, out _, completed: () => completed++);

        await vm.SkipCommand.ExecuteAsync(null);

        Assert.Equal(1, completed);
        var data = await new JsonOnboardingStateStore(temp.Path).LoadAsync(CancellationToken.None);
        Assert.True(data.OnboardingSkipped);
    }

    [Fact]
    public async Task ViewModel_Reopen_ResetsToFirstStep()
    {
        using var temp = new TempDirectory();
        var vm = CreateVm(temp.Path, out _);
        await vm.NextCommand.ExecuteAsync(null);

        await vm.ReopenAsync(CancellationToken.None);

        Assert.Equal(0, vm.CurrentStep);
        Assert.False(await vm.IsCompletedAsync(CancellationToken.None));
    }

    [Fact]
    public void ViewModel_Source_DoesNotReferenceSettingsStores()
    {
        string source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "ViewModels", "OnboardingViewModel.cs"));

        // 引导 VM 只写引导状态，绝不读写通知/登录启动/托盘/自动刷新设置，
        // 因此进入引导不会自动开启任何功能。
        Assert.DoesNotContain("StartupTaskService", source);
        Assert.DoesNotContain("NotificationSettingsStore", source);
        Assert.DoesNotContain("TraySettingsStore", source);
        Assert.DoesNotContain("MonitoringScheduler", source);
    }

    [Fact]
    public async Task MainViewModel_NotCompleted_NavigatesToOnboarding()
    {
        using var temp = new TempDirectory();
        var accountManager = new FakeAccountManager();
        var mainVm = new MainViewModel(
            accountManager,
            new FakeDialogService(),
            new AppLog(Path.GetTempPath()),
            new FakeClipboardService(),
            new FakeUiThreadInvoker());
        var onboardingVm = CreateVm(temp.Path, out _);
        mainVm.Onboarding = onboardingVm;

        await mainVm.EnsureOnboardingAsync(CancellationToken.None);

        Assert.Equal(AppPageKind.Onboarding, mainVm.CurrentPage);
    }

    [Fact]
    public async Task MainViewModel_Completed_StaysOnHome()
    {
        using var temp = new TempDirectory();
        var store = new JsonOnboardingStateStore(temp.Path);
        await store.MarkCompletedAsync(skipped: false, CancellationToken.None);
        var mainVm = new MainViewModel(
            new FakeAccountManager(),
            new FakeDialogService(),
            new AppLog(Path.GetTempPath()),
            new FakeClipboardService(),
            new FakeUiThreadInvoker());
        mainVm.Onboarding = new OnboardingViewModel(
            store,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => { });

        await mainVm.EnsureOnboardingAsync(CancellationToken.None);

        Assert.Equal(AppPageKind.Home, mainVm.CurrentPage);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ApiMonitor.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录。");
    }

    private static OnboardingViewModel CreateVm(
        string dataDirectory,
        out JsonOnboardingStateStore store,
        Action? completed = null)
    {
        store = new JsonOnboardingStateStore(dataDirectory);
        return new OnboardingViewModel(
            store,
            completeCallback: () =>
            {
                completed?.Invoke();
                return Task.CompletedTask;
            },
            addAccountCallback: () => Task.CompletedTask,
            openSettingsCallback: () => { });
    }
}
