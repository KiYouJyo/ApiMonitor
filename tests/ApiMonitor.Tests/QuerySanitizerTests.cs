using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class QuerySanitizerTests
{
    [Theory]
    [InlineData("key")]
    [InlineData("ak")]
    [InlineData("tk")]
    [InlineData("sig")]
    [InlineData("sn")]
    [InlineData("token")]
    [InlineData("authorization")]
    [InlineData("password")]
    [InlineData("sk")]
    public void SensitiveParameterNames_AreDetected(string name) =>
        Assert.True(QuerySanitizer.IsSensitiveParameter(name));

    [Fact]
    public void SafeRequestTarget_StripsQueryAndFragment()
    {
        var uri = new Uri(
            "https://api.map.baidu.com/geocoding/v3/?address=x&ak=SECRET&output=json#frag");

        string target = QuerySanitizer.SafeRequestTarget(uri);

        Assert.Equal("https://api.map.baidu.com/geocoding/v3/", target);
        Assert.DoesNotContain("SECRET", target);
    }

    [Fact]
    public void SanitizeQuery_RemovesSensitiveParameters_KeepsOthers()
    {
        var uri = new Uri(
            "https://restapi.amap.com/v3/geocode/geo?address=%E5%8C%97%E4%BA%AC&key=K&output=json&sig=S");

        string query = QuerySanitizer.SanitizeQuery(uri);

        Assert.Contains("address=", query);
        Assert.Contains("output=json", query);
        Assert.DoesNotContain("key=", query);
        Assert.DoesNotContain("sig=", query);
        Assert.DoesNotContain("K", query.Replace("%E5%8C%97%E4%BA%AC", string.Empty));
    }
}
