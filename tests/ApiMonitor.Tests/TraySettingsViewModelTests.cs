using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// “通知区域与启动”设置区测试（需求：系统 StartupTask 状态是权威来源、
/// 本地设置不覆盖系统状态、DisabledByUser/DisabledByPolicy 显示明确提示、
/// 开关操作调用 Enable/Disable 并持久化偏好）。
/// </summary>
public sealed class TraySettingsViewModelTests
{
    private sealed class Harness
    {
        public FakeTraySettingsStore SettingsStore { get; } = new();

        public FakeStartupTaskService StartupTask { get; } = new();

        public FakeExitCoordinator Exit { get; } = new();

        public TraySettingsViewModel CreateSut()
        {
            var vm = new TraySettingsViewModel(SettingsStore, StartupTask, Exit);
            return vm;
        }
    }

    [Fact]
    public async Task Initialize_LoadsCloseBehaviorPreference()
    {
        var harness = new Harness();
        harness.SettingsStore.Settings.MainWindowCloseBehavior = MainWindowCloseBehavior.ExitApplication;
        var sut = harness.CreateSut();

        await sut.InitializeAsync();

        Assert.Equal(MainWindowCloseBehavior.ExitApplication, sut.CloseBehavior);
        Assert.Equal(1, sut.CloseBehaviorIndex);
    }

    [Fact]
    public async Task Initialize_ReflectsSystemStartupStatus()
    {
        var harness = new Harness();
        harness.StartupTask.RefreshResult = StartupTaskStatus.Enabled;
        var sut = harness.CreateSut();

        await sut.InitializeAsync();

        Assert.True(sut.StartWithWindows);
        Assert.Equal(StartupTaskStatus.Enabled, harness.StartupTask.CachedStatus);
    }

    [Fact]
    public async Task ToggleOn_CallsEnableAndSavesPreference()
    {
        var harness = new Harness();
        harness.StartupTask.RefreshResult = StartupTaskStatus.Disabled;
        harness.StartupTask.EnableResult = StartupTaskStatus.Enabled;
        var sut = harness.CreateSut();
        await sut.InitializeAsync();

        sut.StartWithWindows = true;
        await WaitForBusyToSettleAsync(sut);

        Assert.Equal(1, harness.StartupTask.EnableCalls);
        Assert.Equal(0, harness.StartupTask.DisableCalls);
        Assert.True(sut.StartWithWindows);
        Assert.True(harness.SettingsStore.Settings.StartWithWindows);
        Assert.Equal(StartupTaskStatus.Enabled, harness.SettingsStore.Settings.LastKnownStartupTaskState);
    }

    [Fact]
    public async Task ToggleOff_CallsDisableAndSavesPreference()
    {
        var harness = new Harness();
        harness.StartupTask.RefreshResult = StartupTaskStatus.Enabled;
        harness.StartupTask.DisableResult = StartupTaskStatus.Disabled;
        var sut = harness.CreateSut();
        await sut.InitializeAsync();
        Assert.True(sut.StartWithWindows);

        sut.StartWithWindows = false;
        await WaitForBusyToSettleAsync(sut);

        Assert.Equal(1, harness.StartupTask.DisableCalls);
        Assert.False(sut.StartWithWindows);
        Assert.False(harness.SettingsStore.Settings.StartWithWindows);
    }

    [Fact]
    public async Task DisabledByPolicy_ShowsPolicyHintAndStaysOff()
    {
        var harness = new Harness();
        harness.StartupTask.RefreshResult = StartupTaskStatus.DisabledByPolicy;
        harness.StartupTask.EnableResult = StartupTaskStatus.DisabledByPolicy; // 策略阻止启用
        var sut = harness.CreateSut();
        await sut.InitializeAsync();

        sut.StartWithWindows = true;
        await WaitForBusyToSettleAsync(sut);

        Assert.False(sut.StartWithWindows);
        Assert.Contains("策略", sut.StartupTaskStatusText);
        Assert.True(sut.HasStartupTaskStatusText);
    }

    [Fact]
    public async Task DisabledByUser_ShowsUserDisabledHint()
    {
        var harness = new Harness();
        harness.StartupTask.RefreshResult = StartupTaskStatus.DisabledByUser;
        var sut = harness.CreateSut();
        await sut.InitializeAsync();

        Assert.False(sut.StartWithWindows);
        Assert.Contains("Windows 启动应用设置", sut.StartupTaskStatusText);
    }

    [Fact]
    public async Task LocalPreference_DoesNotOverrideSystemStatus()
    {
        var harness = new Harness();
        harness.SettingsStore.Settings.StartWithWindows = true; // 本地偏好为开
        harness.StartupTask.RefreshResult = StartupTaskStatus.Disabled; // 但系统是关
        var sut = harness.CreateSut();

        await sut.InitializeAsync();

        // 开关反映系统权威状态，而不是本地偏好。
        Assert.False(sut.StartWithWindows);
    }

    [Fact]
    public void ExitCommand_TriggersCoordinator()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();

        sut.ExitApplicationCommand.Execute(null);

        Assert.Equal(1, harness.Exit.BeginExitCalls);
    }

    private static async Task WaitForBusyToSettleAsync(TraySettingsViewModel viewModel)
    {
        for (int i = 0; i < 50 && viewModel.IsStartupTaskBusy; i++)
        {
            await Task.Delay(100);
        }
    }
}
