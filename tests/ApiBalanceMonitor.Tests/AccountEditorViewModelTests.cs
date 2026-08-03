using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Tests.TestDoubles;
using ApiBalanceMonitor.ViewModels;
using Xunit;

namespace ApiBalanceMonitor.Tests;

public sealed class AccountEditorViewModelTests
{
    private static AccountEditorContext CreateContext(bool editing = false, bool hasCredential = false)
    {
        var manager = new FakeAccountManager();
        return new AccountEditorContext
        {
            AccountId = editing ? "acct-1" : null,
            Providers = manager.Providers,
            InitialProviderId = "deepseek",
            InitialDisplayName = editing ? "Test" : string.Empty,
            HasStoredCredential = hasCredential,
        };
    }

    [Fact]
    public void Constructor_DoesNotThrow_ForNewAndEditContexts()
    {
        var manager = new FakeAccountManager();

        var newVm = new AccountEditorViewModel(manager, CreateContext());
        var editVm = new AccountEditorViewModel(
            manager,
            CreateContext(editing: true, hasCredential: true));

        Assert.False(newVm.CanSave);
        Assert.False(newVm.CanTest);
        Assert.True(editVm.CanSave);
        Assert.True(editVm.CanTest);
    }

    [Fact]
    public void CanSave_RequiresDisplayNameAndApiKeyForNewAccount()
    {
        var vm = new AccountEditorViewModel(new FakeAccountManager(), CreateContext());

        vm.DisplayName = "My DeepSeek";
        Assert.False(vm.CanSave);
        Assert.False(vm.CanTest);

        vm.ApiKey = "sk-test-only";
        Assert.True(vm.CanSave);
        Assert.True(vm.CanTest);
    }

    [Fact]
    public void TryBuildResult_ReturnsNullKeyWhenKeepingExistingCredential()
    {
        var vm = new AccountEditorViewModel(
            new FakeAccountManager(),
            CreateContext(editing: true, hasCredential: true));

        bool ok = vm.TryBuildResult(out var result);

        Assert.True(ok);
        Assert.NotNull(result);
        Assert.True(result!.SaveRequested);
        Assert.Null(result.ApiKey);
        Assert.Equal("Test", result.DisplayName);
    }
}
