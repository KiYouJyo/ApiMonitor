using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class DiagnosticsInfoServiceTests
{
    [Fact]
    public async Task Build_ContainsOnlyNonSensitiveFields()
    {
        using var temp = new TempDirectory();
        var accountManager = new FakeAccountManager();
        accountManager.Accounts.Add(new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "敏感显示名",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        var service = new DiagnosticsInfoService(
            accountManager,
            new JsonNotificationStateStore(temp.Path),
            new FakeStartupTaskService(),
            language: "zh-CN",
            theme: "System");

        string diagnostics = await service.BuildAsync(CancellationToken.None);

        Assert.Contains("DisplayVersion:", diagnostics);
        Assert.Contains("PackageVersion:", diagnostics);
        Assert.Contains("Architecture:", diagnostics);
        Assert.Contains("ProviderIds:", diagnostics);
        Assert.Contains("AccountCount: 1", diagnostics);

        // 禁止包含敏感字段。
        Assert.DoesNotContain("敏感显示名", diagnostics);
        Assert.DoesNotContain("sk-", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credential Locker Resource", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acct-1", diagnostics);
    }

    private sealed class FakeStartupTaskService : IStartupTaskService
    {
        public StartupTaskStatus? CachedStatus { get; } = StartupTaskStatus.Disabled;

        public Task<StartupTaskStatus> RefreshStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(StartupTaskStatus.Disabled);

        public Task<StartupTaskStatus> EnableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(StartupTaskStatus.Disabled);

        public Task<StartupTaskStatus> DisableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(StartupTaskStatus.Disabled);
    }
}
