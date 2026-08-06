using ApiMonitor.Services;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v1.0.0：明确取消跨包数据迁移。
/// Store 版按全新安装处理：不检测旧 Package Family、不读取旧 LocalState、
/// 不迁移 Credential Locker、不存在迁移向导、不复制旧余额/设置。
/// 测试只做源码/行为契约检查，不调用真实 Store 或真实 Provider。
/// </summary>
public sealed class NoMigrationTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void StoreIdentity_DoesNotReferenceOldSideloadPackageFamily()
    {
        string channelSource = File.ReadAllText(
            Path.Combine(RepoRoot, "Services", "DistributionChannel.cs"));

        // Store 官方身份来自 Partner Center；不得包含旧侧载 PFN 或把旧身份
        // 用作 Store 迁移源。
        Assert.DoesNotContain("cx0n152q1hsh2", channelSource);
        Assert.DoesNotContain("migration", channelSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Onboarding_DoesNotReadOldLocalState()
    {
        using var temp = new TempDirectory();
        // 模拟旧侧载 LocalState 目录与数据文件存在。
        string oldDir = Path.Combine(temp.Path, "LocalState");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "accounts.json"), """{"schemaVersion":3,"accounts":[]}""");
        File.WriteAllText(Path.Combine(temp.Path, "accounts.json"), """{"schemaVersion":3,"accounts":[]}""");

        var store = new JsonOnboardingStateStore(temp.Path);
        var data = await store.LoadAsync(CancellationToken.None);

        // 引导状态只读取 onboarding.json；旧目录/旧文件不影响首次启动判定。
        Assert.False(data.OnboardingCompleted);
    }

    [Fact]
    public void CredentialStore_OnlyUsesCurrentPackageResource()
    {
        string source = File.ReadAllText(
            Path.Combine(RepoRoot, "Services", "CredentialLockerSecretStore.cs"));

        // 凭据资源名固定为当前应用；不得枚举其他包/其他 Resource 的凭据。
        Assert.Contains("const string ResourceName = \"ApiMonitor\"", source);
        Assert.DoesNotContain("FindAllByResource", source);
    }

    [Fact]
    public void NoMigrationWizardTypes_ExistInAppSource()
    {
        foreach (string pattern in new[]
        {
            "MigrationWizard",
            "MigrationHelper",
            "LegacyImport",
            "OldPackageImporter",
            "MigrationCompletedFlag",
        })
        {
            var hits = Directory.EnumerateFiles(RepoRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.Combine("tests")) && !f.Contains(Path.Combine("bin")) && !f.Contains(Path.Combine("obj")))
                .Where(f => File.ReadAllText(f).Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(hits.Count == 0, $"发现疑似迁移组件类型名：{pattern} -> {string.Join(", ", hits)}");
        }
    }

    [Fact]
    public void StoreValidationScript_ForbidsSideloadToolsAndSecrets()
    {
        string script = File.ReadAllText(
            Path.Combine(RepoRoot, "packaging", "Test-StorePackageIdentity.ps1"));

        Assert.DoesNotContain("SafeLocalStateBackup.ps1", script);
        Assert.DoesNotContain("ApiMonitorDev.cer", script);
        Assert.DoesNotContain("Install.cmd", script);
        Assert.DoesNotContain("Uninstall.cmd", script);
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
}
