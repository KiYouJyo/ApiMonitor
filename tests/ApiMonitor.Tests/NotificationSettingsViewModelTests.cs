using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class NotificationSettingsViewModelTests
{
    private sealed class CallCounters
    {
        public int TestCalls { get; set; }

        public int WindowSettingsOpened { get; set; }
    }

    private static NotificationSettingsViewModel CreateSut(
        FakeNotificationSettingsStore store,
        CallCounters? counters = null)
    {
        counters ??= new CallCounters();
        var vm = new NotificationSettingsViewModel(
            store,
            () => counters.TestCalls++,
            () => counters.WindowSettingsOpened++,
            new AppLog(System.IO.Path.GetTempPath()));
        return vm;
    }

    [Fact]
    public async Task Initialize_LoadsRealCurrentStateFromStore()
    {
        var store = new FakeNotificationSettingsStore
        {
            Settings = new NotificationGlobalSettings
            {
                BalanceNotificationsEnabled = true,
                DefaultRepeatIntervalHours = 12,
                RecoveryNotificationsEnabled = false,
            },
        };
        var vm = CreateSut(store);

        await vm.InitializeAsync();

        Assert.True(vm.BalanceNotificationsEnabled);
        Assert.Equal(12, vm.DefaultRepeatIntervalHours);
        Assert.False(vm.RecoveryNotificationsEnabled);
    }

    [Fact]
    public async Task SettingChange_PersistsToStore()
    {
        var store = new FakeNotificationSettingsStore();
        var vm = CreateSut(store);
        await vm.InitializeAsync();

        vm.BalanceNotificationsEnabled = true;
        vm.DefaultRepeatIntervalHours = 72;
        vm.RecoveryNotificationsEnabled = false;

        // 保存是异步的：轮询等待写入。
        for (int i = 0; i < 100 && !store.Settings.BalanceNotificationsEnabled; i++)
        {
            await Task.Delay(10);
        }

        Assert.True(store.Settings.BalanceNotificationsEnabled);
        Assert.Equal(72, store.Settings.DefaultRepeatIntervalHours);
        Assert.False(store.Settings.RecoveryNotificationsEnabled);
    }

    [Fact]
    public void TestNotificationCommand_InvokesActionWithoutQueryingProvider()
    {
        var store = new FakeNotificationSettingsStore();
        var counters = new CallCounters();
        var vm = CreateSut(store, counters);

        vm.SendTestCommand.Execute(null);

        // SendTest 命令本身只调用注入的动作；不依赖任何 IAccountManager/Provider。
        Assert.Equal(1, counters.TestCalls);
        Assert.True(vm.SendTestCommand.CanExecute(null));
        Assert.True(vm.HasStatusText);
    }

    [Fact]
    public void RepeatOptions_ContainAllSupportedIntervals()
    {
        var store = new FakeNotificationSettingsStore();
        var vm = CreateSut(store);

        Assert.Equal(NotificationRepeatIntervals.Options.Count, vm.RepeatOptions.Count);
        Assert.Contains(vm.RepeatOptions, o => o.Hours == NotificationRepeatIntervals.None);
        Assert.Contains(vm.RepeatOptions, o => o.Hours == NotificationRepeatIntervals.DefaultHours);
        Assert.Contains(vm.RepeatOptions, o => o.Hours == NotificationRepeatIntervals.ThreeDays);
    }
}
