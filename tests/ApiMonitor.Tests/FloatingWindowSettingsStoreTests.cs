using ApiMonitor.Services;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>悬浮窗设置持久化测试（默认值、旧设置迁移、损坏恢复）。</summary>
public sealed class FloatingWindowSettingsStoreTests
{
    [Fact]
    public async Task Load_WhenFileMissing_ReturnsDefaultsWithAlwaysOnTop()
    {
        using var temp = new TempDirectory();
        var store = new FloatingWindowSettingsStore(temp.Path);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.True(settings.IsAlwaysOnTop);
        Assert.Equal(FloatingWindowSettingsStore.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Null(settings.SelectedAccountId);
        Assert.Equal(FloatingWindowDefaults.DefaultWidth, settings.Width);
        Assert.Equal(FloatingWindowDefaults.DefaultHeight, settings.Height);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsAllValues()
    {
        using var temp = new TempDirectory();
        var store = new FloatingWindowSettingsStore(temp.Path);
        var saved = new ApiMonitor.Models.FloatingWindowSettings
        {
            IsAlwaysOnTop = false,
            SelectedAccountId = "acct-1",
            Width = 320,
            Height = 180,
            X = 120,
            Y = 80,
            LastDisplayId = "display-1",
        };

        await store.SaveAsync(saved, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.False(loaded.IsAlwaysOnTop);
        Assert.Equal("acct-1", loaded.SelectedAccountId);
        Assert.Equal(320, loaded.Width);
        Assert.Equal(180, loaded.Height);
        Assert.Equal(120, loaded.X);
        Assert.Equal(80, loaded.Y);
        Assert.Equal("display-1", loaded.LastDisplayId);
        Assert.Equal(FloatingWindowSettingsStore.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public async Task Load_LegacyCompactSettings_MigratesOnceAndIsIdempotent()
    {
        using var temp = new TempDirectory();
        string legacyPath = Path.Combine(temp.Path, FloatingWindowSettingsStore.LegacyFileName);
        await File.WriteAllTextAsync(legacyPath, """
            {
              "schemaVersion": 3,
              "isAlwaysOnTop": false,
              "selectedAccountId": "acct-keep",
              "selectedCurrency": "USD",
              "selectedMetricId": "deepseek:USD:total",
              "width": 380,
              "height": 220,
              "x": 150,
              "y": 90,
              "lastDisplayId": "display-legacy"
            }
            """);

        var store = new FloatingWindowSettingsStore(temp.Path);
        var first = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(FloatingWindowSettingsStore.CurrentSchemaVersion, first.SchemaVersion);
        Assert.False(first.IsAlwaysOnTop);
        Assert.Equal("acct-keep", first.SelectedAccountId);
        Assert.Equal(380, first.Width);
        Assert.Equal(220, first.Height);
        Assert.Equal(150, first.X);
        Assert.Equal(90, first.Y);
        Assert.Equal("display-legacy", first.LastDisplayId);

        // 旧文件保留，新文件已生成；再次加载不会重复迁移或改写旧文件。
        string legacyAfter = await File.ReadAllTextAsync(legacyPath);
        Assert.Contains("acct-keep", legacyAfter);
        Assert.True(File.Exists(Path.Combine(temp.Path, FloatingWindowSettingsStore.FileName)));

        var second = await store.LoadAsync(CancellationToken.None);
        Assert.Equal("acct-keep", second.SelectedAccountId);
    }

    [Fact]
    public async Task Load_LegacyAndNewFilePresent_NewFileWinsAndLegacyUntouched()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, FloatingWindowSettingsStore.LegacyFileName),
            """{ "schemaVersion": 3, "selectedAccountId": "acct-legacy", "width": 380, "height": 220 }""");
        var store = new FloatingWindowSettingsStore(temp.Path);
        await store.SaveAsync(
            new ApiMonitor.Models.FloatingWindowSettings { SelectedAccountId = "acct-new" },
            CancellationToken.None);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("acct-new", loaded.SelectedAccountId);
        string legacy = await File.ReadAllTextAsync(Path.Combine(temp.Path, FloatingWindowSettingsStore.LegacyFileName));
        Assert.Contains("acct-legacy", legacy);
    }

    [Fact]
    public async Task Load_CorruptFile_BacksUpAndReturnsDefaults()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, FloatingWindowSettingsStore.FileName);
        await File.WriteAllTextAsync(path, "{ broken json !!!");

        var store = new FloatingWindowSettingsStore(temp.Path);
        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.True(settings.IsAlwaysOnTop);
        Assert.Null(settings.SelectedAccountId);
        Assert.Single(Directory.GetFiles(temp.Path, "*.corrupt-*.json"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Load_InvalidDimensions_AreSanitized()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, FloatingWindowSettingsStore.FileName);
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": 1,
              "isAlwaysOnTop": true,
              "width": 99999,
              "height": -5,
              "x": null,
              "y": 0
            }
            """);

        var store = new FloatingWindowSettingsStore(temp.Path);
        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.InRange(settings.Width, FloatingWindowDefaults.MinWidth, FloatingWindowDefaults.MaxWidth);
        Assert.InRange(settings.Height, FloatingWindowDefaults.MinHeight, FloatingWindowDefaults.MaxHeight);
        Assert.Null(settings.X);
    }
}
