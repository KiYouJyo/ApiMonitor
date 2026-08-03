using ApiMonitor.Services;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>紧凑窗口设置持久化测试（默认值、升级、损坏恢复）。</summary>
public sealed class CompactWindowSettingsStoreTests
{
    [Fact]
    public async Task Load_WhenFileMissing_ReturnsDefaultsWithAlwaysOnTop()
    {
        using var temp = new TempDirectory();
        var store = new CompactWindowSettingsStore(temp.Path);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.True(settings.IsAlwaysOnTop);
        Assert.Equal(CompactWindowSettingsStore.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Null(settings.SelectedAccountId);
        Assert.Null(settings.SelectedCurrency);
        Assert.Equal(CompactWindowDefaults.DefaultWidth, settings.Width);
        Assert.Equal(CompactWindowDefaults.DefaultHeight, settings.Height);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsAllValues()
    {
        using var temp = new TempDirectory();
        var store = new CompactWindowSettingsStore(temp.Path);
        var saved = new ApiMonitor.Models.CompactWindowSettings
        {
            IsAlwaysOnTop = false,
            SelectedAccountId = "acct-1",
            SelectedCurrency = "CNY",
            Width = 400,
            Height = 260,
            X = 120,
            Y = 80,
            LastDisplayId = "display-1",
        };

        await store.SaveAsync(saved, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.False(loaded.IsAlwaysOnTop);
        Assert.Equal("acct-1", loaded.SelectedAccountId);
        Assert.Equal("CNY", loaded.SelectedCurrency);
        Assert.Equal(400, loaded.Width);
        Assert.Equal(260, loaded.Height);
        Assert.Equal(120, loaded.X);
        Assert.Equal(80, loaded.Y);
        Assert.Equal("display-1", loaded.LastDisplayId);
        Assert.Equal(CompactWindowSettingsStore.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public async Task Load_LowerSchemaVersion_UpgradesToCurrent()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, CompactWindowSettingsStore.FileName);
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": 1,
              "isAlwaysOnTop": false,
              "selectedAccountId": "acct-keep",
              "selectedCurrency": "USD",
              "width": 380,
              "height": 220
            }
            """);

        var store = new CompactWindowSettingsStore(temp.Path);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(CompactWindowSettingsStore.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.False(loaded.IsAlwaysOnTop);
        Assert.Equal("acct-keep", loaded.SelectedAccountId);
        Assert.Equal("USD", loaded.SelectedCurrency);

        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"schemaVersion\": 3", json);
    }

    [Fact]
    public async Task Load_CorruptFile_BacksUpAndReturnsDefaults()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, CompactWindowSettingsStore.FileName);
        await File.WriteAllTextAsync(path, "{ broken json !!!");

        var store = new CompactWindowSettingsStore(temp.Path);
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
        string path = Path.Combine(temp.Path, CompactWindowSettingsStore.FileName);
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": 3,
              "isAlwaysOnTop": true,
              "width": 99999,
              "height": -5,
              "x": null,
              "y": 0
            }
            """);

        var store = new CompactWindowSettingsStore(temp.Path);
        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.InRange(settings.Width, CompactWindowDefaults.MinWidth, CompactWindowDefaults.MaxWidth);
        Assert.InRange(settings.Height, CompactWindowDefaults.MinHeight, CompactWindowDefaults.MaxHeight);
        Assert.Null(settings.X);
    }
}
