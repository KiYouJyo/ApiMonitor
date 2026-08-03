using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class MainViewModelNavigationTests
{
    private static MainViewModel CreateVm(out FakeAccountManager accountManager)
    {
        accountManager = new FakeAccountManager();
        var vm = new MainViewModel(
            accountManager,
            new FakeDialogService(),
            new AppLog(Path.GetTempPath()),
            new FakeClipboardService(),
            new FakeUiThreadInvoker());
        return vm;
    }

    [Fact]
    public void NavigateTo_ChangesCurrentPage()
    {
        var vm = CreateVm(out _);

        vm.NavigateTo(AppPageKind.Insights);
        Assert.Equal(AppPageKind.Insights, vm.CurrentPage);

        vm.NavigateTo(AppPageKind.About);
        Assert.Equal(AppPageKind.About, vm.CurrentPage);

        vm.NavigateTo(AppPageKind.Settings);
        Assert.Equal(AppPageKind.Settings, vm.CurrentPage);

        vm.NavigateTo(AppPageKind.Home);
        Assert.Equal(AppPageKind.Home, vm.CurrentPage);
    }

    [Fact]
    public void OpenInsightsForAccount_SetsTargetAndNavigates()
    {
        var vm = CreateVm(out _);

        vm.OpenInsightsForAccount("acct-1");

        Assert.Equal(AppPageKind.Insights, vm.CurrentPage);
        Assert.Equal("acct-1", vm.InsightsTargetAccountId);
    }

    [Fact]
    public void FocusAccount_ForcesHomeNavigation()
    {
        var vm = CreateVm(out _);
        vm.NavigateTo(AppPageKind.Settings);

        vm.FocusAccount("acct-1");

        Assert.Equal(AppPageKind.Home, vm.CurrentPage);
    }

    [Fact]
    public void InsightsTargetAccountId_UsesStableIdNotDisplayName()
    {
        var vm = CreateVm(out _);

        vm.OpenInsightsForAccount("acct-ABC-123");

        // 主键是 AccountId，不依赖账户名称。
        Assert.Equal("acct-ABC-123", vm.InsightsTargetAccountId);
    }
}
