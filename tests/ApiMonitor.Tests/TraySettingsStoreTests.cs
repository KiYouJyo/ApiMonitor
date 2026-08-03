using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 托盘设置存储测试（需求：schemaVersion 3 迁移到 4、缺失字段默认值、
/// 损坏恢复默认、设置文件不含密钥、迁移幂等）。
/// </summary>
public sealed class TraySettingsStoreTests
{
    private static JsonTraySettingsStore CreateStore(TempDirectory dir) =>
        new(dir.Path);

    [Fact]
    public async Task NoFile_ReturnsDefaults()
    {
        using var dir = new TempDirectory();
        var store = CreateStore(dir);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(JsonTraySettingsStore.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(MainWindowCloseBehavior.HideToTray, settings.MainWindowCloseBehavior);
        Assert.True(settings.ShowFirstCloseExplanation);
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public async Task SchemaVersion3_MigratesTo4KeepingKnownFields()
    {
        using var dir = new TempDirectory();
        var path = System.IO.Path.Combine(dir.Path, JsonTraySettingsStore.FileName);
        // 模拟 v0.3.x 的低版本设置：只包含部分已知字段。
        await System.IO.File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 3,
              "mainWindowCloseBehavior": 1,
              "showFirstCloseExplanation": false,
              "startWithWindows": true
            }
            """);

        var store = CreateStore(dir);
        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(4, settings.SchemaVersion);
        Assert.Equal(MainWindowCloseBehavior.ExitApplication, settings.MainWindowCloseBehavior);
        Assert.False(settings.ShowFirstCloseExplanation);
        Assert.True(settings.StartWithWindows);
    }

    [Fact]
    public async Task CorruptFile_IsBackedUpAndDefaultsRestored()
    {
        using var dir = new TempDirectory();
        var path = System.IO.Path.Combine(dir.Path, JsonTraySettingsStore.FileName);
        await System.IO.File.WriteAllTextAsync(path, "{ not valid json !!!");

        var store = CreateStore(dir);
        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(MainWindowCloseBehavior.HideToTray, settings.MainWindowCloseBehavior);
        Assert.True(settings.ShowFirstCloseExplanation);
        Assert.Contains(
            System.IO.Directory.GetFiles(dir.Path),
            f => f.Contains("corrupt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InvalidEnumValue_SanitizesToDefault()
    {
        using var dir = new TempDirectory();
        var path = System.IO.Path.Combine(dir.Path, JsonTraySettingsStore.FileName);
        await System.IO.File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 4,
              "mainWindowCloseBehavior": 99
            }
            """);

        var store = CreateStore(dir);
        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(MainWindowCloseBehavior.HideToTray, settings.MainWindowCloseBehavior);
    }

    [Fact]
    public async Task HigherSchemaVersion_IsResetToDefaults()
    {
        using var dir = new TempDirectory();
        var path = System.IO.Path.Combine(dir.Path, JsonTraySettingsStore.FileName);
        await System.IO.File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 99,
              "mainWindowCloseBehavior": 1
            }
            """);

        var store = CreateStore(dir);
        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(4, settings.SchemaVersion);
        Assert.Equal(MainWindowCloseBehavior.HideToTray, settings.MainWindowCloseBehavior);
    }

    [Fact]
    public async Task SerializedFile_ContainsNoSecretFields()
    {
        using var dir = new TempDirectory();
        var store = CreateStore(dir);
        var settings = new TraySettings();
        await store.SaveAsync(settings, CancellationToken.None);

        string json = await System.IO.File.ReadAllTextAsync(System.IO.Path.Combine(dir.Path, JsonTraySettingsStore.FileName));
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }
}
