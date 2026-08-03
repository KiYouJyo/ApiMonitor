using System.Net;
using System.Text;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// “刷新全部账户”测试（需求：只刷新存在且有凭据的账户、复用账户级并发锁、
/// 一个失败不影响其他、失败不清空旧余额、取消停止剩余）。
/// </summary>
public sealed class RefreshAllTests
{
    private const string SuccessBody =
        """{ "is_available": true, "balance_infos": [ { "currency": "CNY", "total_balance": "9.90", "granted_balance": "0", "topped_up_balance": "9.90" } ] }""";

    private static (AccountManager Manager, TempDirectory Temp) CreateManager(FakeHttpRequestService http)
    {
        var temp = new TempDirectory();
        var secrets = new FakeSecretStore();
        var time = new FakeTimeProvider();
        var provider = new DeepSeekBalanceProvider(http);
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { provider });
        var manager = new AccountManager(
            new JsonAccountStore(temp.Path),
            new JsonBalanceSnapshotStore(temp.Path),
            secrets,
            registry,
            new AppLog(temp.Path),
            time);
        return (manager, temp);
    }

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(SuccessBody, Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task RefreshAll_RefreshesOnlyCredentialedAccounts()
    {
        var http = FakeHttpRequestService.Returning(SuccessBody);
        var (manager, _) = CreateManager(http);
        await manager.SaveAccountAsync("a1", "deepseek", "A", "key-1", null, new MonitoringSettings(), CancellationToken.None);
        await manager.SaveAccountAsync("a2", "deepseek", "B", "key-2", null, new MonitoringSettings(), CancellationToken.None);
        await manager.SaveAccountAsync("a3", "deepseek", "C", null, null, new MonitoringSettings(), CancellationToken.None);

        await manager.RefreshAllAccountsAsync(BalanceQuerySource.Manual, CancellationToken.None);
        await Task.Delay(1500); // 等待错峰 fire-and-forget 完成。

        var r1 = await manager.GetRecordAsync("a1", CancellationToken.None);
        var r2 = await manager.GetRecordAsync("a2", CancellationToken.None);
        var r3 = await manager.GetRecordAsync("a3", CancellationToken.None);
        Assert.NotNull(r1!.LastQueryAttemptAt);
        Assert.NotNull(r2!.LastQueryAttemptAt);
        Assert.Null(r3!.LastQueryAttemptAt);
    }

    [Fact]
    public async Task RefreshAll_OneFailureDoesNotBlockOthers()
    {
        var http = FakeHttpRequestService.Returning(SuccessBody);
        int call = 0;
        http.SetHandler((_, _) =>
        {
            // 第一个请求（a1）失败，后续（a2）成功。
            return Task.FromResult(Interlocked.Increment(ref call) == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : Ok());
        });
        var (manager, _) = CreateManager(http);
        await manager.SaveAccountAsync("a1", "deepseek", "A", "key-1", null, new MonitoringSettings(), CancellationToken.None);
        await manager.SaveAccountAsync("a2", "deepseek", "B", "key-2", null, new MonitoringSettings(), CancellationToken.None);

        await manager.RefreshAllAccountsAsync(BalanceQuerySource.Manual, CancellationToken.None);
        await Task.Delay(1500);

        var r1 = await manager.GetRecordAsync("a1", CancellationToken.None);
        var r2 = await manager.GetRecordAsync("a2", CancellationToken.None);
        Assert.Null(r1!.LastSuccessfulSnapshot);
        Assert.NotNull(r2!.LastSuccessfulSnapshot);
        Assert.Equal(9.90m, r2.LastSuccessfulSnapshot!.Metrics[0].AvailableAmount);
    }

    [Fact]
    public async Task RefreshAll_FailureKeepsLastSuccessfulSnapshot()
    {
        var http = FakeHttpRequestService.Returning(SuccessBody);
        var (manager, _) = CreateManager(http);
        await manager.SaveAccountAsync("a1", "deepseek", "A", "key-1", null, new MonitoringSettings(), CancellationToken.None);

        // 第一次成功，建立旧快照。
        await manager.RefreshAccountAsync("a1", BalanceQuerySource.Manual, CancellationToken.None);

        // 第二次失败：旧快照保留。
        http.SetHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        await manager.RefreshAllAccountsAsync(BalanceQuerySource.Manual, CancellationToken.None);
        await Task.Delay(1500);

        var record = await manager.GetRecordAsync("a1", CancellationToken.None);
        Assert.NotNull(record!.LastSuccessfulSnapshot);
        Assert.Equal(9.90m, record.LastSuccessfulSnapshot!.Metrics[0].AvailableAmount);
    }

    [Fact]
    public async Task RefreshAll_CancellationReturnsCleanly()
    {
        var http = FakeHttpRequestService.Returning(SuccessBody);
        var (manager, _) = CreateManager(http);
        await manager.SaveAccountAsync("a1", "deepseek", "A", "key-1", null, new MonitoringSettings(), CancellationToken.None);
        await manager.SaveAccountAsync("a2", "deepseek", "B", "key-2", null, new MonitoringSettings(), CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(300);
        await manager.RefreshAllAccountsAsync(BalanceQuerySource.Manual, cts.Token);

        // 取消后方法干净返回，不抛出。
        var r1 = await manager.GetRecordAsync("a1", CancellationToken.None);
        Assert.NotNull(r1);
    }
}
