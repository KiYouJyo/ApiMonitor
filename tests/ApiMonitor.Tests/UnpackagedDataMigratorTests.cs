using ApiMonitor.Services;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 未打包数据目录一次性迁移测试：只使用临时目录，不触碰真实用户数据。
/// </summary>
public sealed class UnpackagedDataMigratorTests
{
    [Fact]
    public void MigrateOnce_MovesLegacyDirectoryToTarget()
    {
        using var root = new TempDirectory();
        string legacy = Path.Combine(root.Path, AppPaths.LegacyUnpackagedDirectoryName);
        string target = Path.Combine(root.Path, AppPaths.UnpackagedDirectoryName);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "accounts.json"), "{}");

        UnpackagedDataMigrator.MigrateOnce(target);

        Assert.True(Directory.Exists(target));
        Assert.True(File.Exists(Path.Combine(target, "accounts.json")));
        Assert.False(Directory.Exists(legacy));
    }

    [Fact]
    public void MigrateOnce_TargetAlreadyExists_LeavesBothUntouched()
    {
        using var root = new TempDirectory();
        string legacy = Path.Combine(root.Path, AppPaths.LegacyUnpackagedDirectoryName);
        string target = Path.Combine(root.Path, AppPaths.UnpackagedDirectoryName);
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "accounts.json"), "new");

        UnpackagedDataMigrator.MigrateOnce(target);

        Assert.True(Directory.Exists(legacy));
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "accounts.json")));
    }

    [Fact]
    public void MigrateOnce_NeitherExists_IsNoOp()
    {
        using var root = new TempDirectory();
        string target = Path.Combine(root.Path, AppPaths.UnpackagedDirectoryName);

        UnpackagedDataMigrator.MigrateOnce(target);

        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void MigrateOnce_SecondCall_IsNoOp()
    {
        using var root = new TempDirectory();
        string legacy = Path.Combine(root.Path, AppPaths.LegacyUnpackagedDirectoryName);
        string target = Path.Combine(root.Path, AppPaths.UnpackagedDirectoryName);
        Directory.CreateDirectory(legacy);

        UnpackagedDataMigrator.MigrateOnce(target);
        UnpackagedDataMigrator.MigrateOnce(target);

        Assert.True(Directory.Exists(target));
        Assert.False(Directory.Exists(legacy));
    }
}
