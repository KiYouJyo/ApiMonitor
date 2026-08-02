using ApiBalanceMonitor.Providers;
using ApiBalanceMonitor.Services;
using ApiBalanceMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiBalanceMonitor.Tests;

public sealed class ProviderRegistryTests
{
    [Fact]
    public void GetById_ReturnsDeepSeekProvider()
    {
        var deepSeek = new DeepSeekBalanceProvider(new HttpRequestService(TimeSpan.FromSeconds(15)));
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { deepSeek });

        var provider = registry.GetById("deepseek");

        Assert.NotNull(provider);
        Assert.Same(deepSeek, provider);
        Assert.Equal("deepseek", provider.ProviderId);
        Assert.Equal("DeepSeek", provider.DisplayName);
    }

    [Fact]
    public void GetById_IsCaseInsensitive()
    {
        var deepSeek = new DeepSeekBalanceProvider(new HttpRequestService(TimeSpan.FromSeconds(15)));
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { deepSeek });

        Assert.NotNull(registry.GetById("DeepSeek"));
    }

    [Fact]
    public void GetById_UnknownProvider_ReturnsNull()
    {
        var registry = new ProviderRegistry(Array.Empty<IApiBalanceProvider>());

        Assert.Null(registry.GetById("unknown"));
        Assert.Null(registry.GetById(string.Empty));
    }
}
