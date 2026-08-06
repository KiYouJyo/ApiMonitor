using System.Xml.Linq;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v1.0.0：Store 包配置契约测试。
/// Store manifest 使用 Partner Center 官方身份；版本 1.0.0.0；
/// 三语资源齐全；Store 输出不得包含侧载工具/开发证书/私钥/日志/用户数据；
/// GitHub 侧载 manifest 保持开发身份。
/// </summary>
public sealed class StorePackageContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void StoreManifest_UsesOfficialIdentity_AndVersion1000()
    {
        string path = Path.Combine(RepoRoot, "Package.Store.appxmanifest");
        Assert.True(File.Exists(path), "缺少 Package.Store.appxmanifest。");
        var doc = XDocument.Load(path);
        var identity = ElementByName(doc.Root!, "Identity");

        Assert.Equal("JoKiy.ApiMonitor", identity.Attribute("Name")?.Value);
        Assert.Equal(
            "CN=C4E4B33A-7B77-4121-897C-7D720A5471F8",
            identity.Attribute("Publisher")?.Value);
        Assert.Equal("1.0.0.0", identity.Attribute("Version")?.Value);
    }

    [Fact]
    public void StoreManifest_ContainsThreeLanguages_AndNoSideloadTools()
    {
        string path = Path.Combine(RepoRoot, "Package.Store.appxmanifest");
        string manifest = File.ReadAllText(path);

        foreach (string language in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            Assert.Contains($"Resource Language=\"{language}\"", manifest);
        }

        Assert.DoesNotContain("ApiMonitorDev", manifest);
        Assert.DoesNotContain("Install.cmd", manifest);
        Assert.DoesNotContain("Uninstall.cmd", manifest);
    }

    [Fact]
    public void SideloadManifest_KeepsDevelopmentIdentity()
    {
        string path = Path.Combine(RepoRoot, "Package.appxmanifest");
        var doc = XDocument.Load(path);
        var identity = ElementByName(doc.Root!, "Identity");

        Assert.Equal("ApiMonitor", identity.Attribute("Name")?.Value);
        Assert.Equal("CN=ApiMonitorDev", identity.Attribute("Publisher")?.Value);
    }

    [Fact]
    public void StorePackageScript_OutputsToStoreDirectory_AndForbidsSensitiveFiles()
    {
        string script = File.ReadAllText(
            Path.Combine(RepoRoot, "packaging", "New-StorePackage.ps1"));

        Assert.Contains("packaging\\output\\v1.0.0\\store", script);
        Assert.Contains(".msixupload", script);
        Assert.Contains("DistributionChannel=MicrosoftStore", script);
        Assert.Contains("AppxPackageSigningEnabled=false", script);
    }

    [Fact]
    public void StoreWorkflow_IsManualOnly_AndContainsNoSecretsOrPublishSteps()
    {
        string workflow = File.ReadAllText(
            Path.Combine(RepoRoot, ".github", "workflows", "store-package.yml"));

        Assert.Contains("workflow_dispatch", workflow);
        Assert.DoesNotContain("client-secret", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PFX", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("msstore publish", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("microsoft-store-apppublisher", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectoryBuildProps_ChannelDefaults_KeepStoreVersionFixedAt1000()
    {
        string props = File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props"));

        Assert.Contains("<ApiMonitorDisplayVersion>1.0.0</ApiMonitorDisplayVersion>", props);
        Assert.Contains("1.0.0.0", props);
        Assert.Contains("DISTRIBUTION_CHANNEL_MICROSOFT_STORE", props);
        Assert.Contains("DISTRIBUTION_CHANNEL_GITHUB_SIDELOAD", props);
        Assert.Contains("DISTRIBUTION_CHANNEL_DEVELOPMENT", props);
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

    private static XElement ElementByName(XElement root, string localName) =>
        root.Elements().First(e => e.Name.LocalName == localName);
}
