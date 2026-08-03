using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 主窗口关闭行为测试（需求：HideToTray 取消销毁并隐藏、ExitApplication 完整退出、
/// 首次说明只显示一次、取消保持窗口、Alt+F4 与关闭按钮一致走统一入口、
/// 退出中放行真正关闭）。
/// </summary>
public sealed class WindowCloseBehaviorTests
{
    private sealed class Harness
    {
        public FakeTraySettingsStore SettingsStore { get; } = new();

        public FakeDialogService Dialogs { get; } = new();

        public FakeExitCoordinator Exit { get; } = new();

        public FakeMainWindowController Window { get; } = new();

        public AppLog Log { get; } = new(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"abm-close-log-{Guid.NewGuid():N}"));

        public WindowCloseBehaviorController CreateSut() =>
            new(SettingsStore, Dialogs, Exit, Window, Log);
    }

    [Fact]
    public async Task HideToTray_HidesWindowInsteadOfClosing()
    {
        var harness = new Harness();
        harness.SettingsStore.Settings.MainWindowCloseBehavior = MainWindowCloseBehavior.HideToTray;
        harness.SettingsStore.Settings.ShowFirstCloseExplanation = false;
        var sut = harness.CreateSut();

        await sut.HandleCloseRequestedAsync();

        Assert.Equal(1, harness.Window.HideCalls);
        Assert.Equal(0, harness.Window.CloseCalls);
        Assert.Equal(0, harness.Exit.BeginExitCalls);
    }

    [Fact]
    public async Task ExitApplication_TriggersFullExit()
    {
        var harness = new Harness();
        harness.SettingsStore.Settings.MainWindowCloseBehavior = MainWindowCloseBehavior.ExitApplication;
        var sut = harness.CreateSut();

        await sut.HandleCloseRequestedAsync();

        Assert.Equal(1, harness.Exit.BeginExitCalls);
        Assert.Equal(0, harness.Window.HideCalls);
    }

    [Fact]
    public async Task FirstExplanation_ShownOnlyOncePerInstance()
    {
        var harness = new Harness();
        harness.SettingsStore.Settings.ShowFirstCloseExplanation = true;
        harness.Dialogs.FirstCloseResult = FirstCloseChoice.Hide;
        var sut = harness.CreateSut();

        await sut.HandleCloseRequestedAsync();
        await sut.HandleCloseRequestedAsync();

        // 对话框只显示一次；第二次直接隐藏。
        Assert.Equal(1, harness.Dialogs.FirstCloseCalls);
        Assert.Equal(2, harness.Window.HideCalls);
    }

    [Fact]
    public async Task Cancel_KeepsWindowVisible()
    {
        var harness = new Harness();
        harness.SettingsStore.Settings.ShowFirstCloseExplanation = true;
        harness.Dialogs.FirstCloseResult = FirstCloseChoice.Cancel;
        var sut = harness.CreateSut();

        await sut.HandleCloseRequestedAsync();

        Assert.Equal(0, harness.Window.HideCalls);
        Assert.Equal(0, harness.Window.CloseCalls);
    }

    [Fact]
    public async Task DontAskAgain_PersistsPreference()
    {
        var harness = new Harness();
        harness.SettingsStore.Settings.ShowFirstCloseExplanation = true;
        harness.Dialogs.FirstCloseResult = FirstCloseChoice.HideAndDontAskAgain;
        var sut = harness.CreateSut();

        await sut.HandleCloseRequestedAsync();

        Assert.False(harness.SettingsStore.Settings.ShowFirstCloseExplanation);
        Assert.True(harness.SettingsStore.SaveCalls >= 1);
        Assert.Equal(1, harness.Window.HideCalls);
    }

    [Fact]
    public async Task IsExiting_AllowsRealClose()
    {
        var harness = new Harness();
        harness.Exit.IsExiting = true;
        var sut = harness.CreateSut();

        await sut.HandleCloseRequestedAsync();

        Assert.True(harness.Window.AllowCloseCalled);
        Assert.Equal(1, harness.Window.CloseCalls);
        Assert.Equal(0, harness.Window.HideCalls);
    }
}
