using ApiMonitor.Services;
using ApiMonitor.ViewModels;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class AppearanceSettingsTests
{
    private sealed class FakeLanguageAdapter : ILanguageSystemAdapter
    {
        public string? Override { get; set; }

        public string? ReadOverride() => Override;

        public void WriteOverride(string? languageCode) => Override = languageCode;
    }

    [Fact]
    public async Task ThemeSelection_PersistsAndReloads()
    {
        using var temp = new TempDirectory();
        var store = new JsonAppearanceSettingsStore(temp.Path);
        var appearance = new AppearanceService();
        var language = new LanguageService(new FakeLanguageAdapter());

        var vm = new AppearanceSettingsViewModel(store, appearance, language);
        await vm.InitializeAsync();
        vm.SelectedTheme = vm.ThemeOptions.First(o => o.Theme == AppThemePreference.Dark);
        await Task.Delay(50); // 等待异步保存

        var reloaded = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(nameof(AppThemePreference.Dark), reloaded.Theme);
    }

    [Fact]
    public void ApplyTheme_NotifiesThemeChanged()
    {
        var appearance = new AppearanceService();
        AppThemePreference? received = null;
        appearance.ThemeChanged += theme => received = theme;

        appearance.ApplyTheme(AppThemePreference.Light);

        Assert.Equal(AppThemePreference.Light, received);
        Assert.Equal(AppThemePreference.Light, appearance.Theme);
    }

    [Fact]
    public void LanguageService_NormalizesCodes()
    {
        Assert.Equal("zh-CN", LanguageService.NormalizeCode("zh-Hans-CN"));
        Assert.Equal("zh-CN", LanguageService.NormalizeCode("zh-CN"));
        Assert.Equal("ja-JP", LanguageService.NormalizeCode("ja"));
        Assert.Equal("en-US", LanguageService.NormalizeCode("en-US"));
        Assert.Equal("en-US", LanguageService.NormalizeCode("fr-FR"));
        Assert.Equal("en-US", LanguageService.NormalizeCode(null));
    }

    [Fact]
    public void LanguageService_ApplyLanguage_WritesOverride()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(adapter);

        service.ApplyLanguage(AppLanguagePreference.ZhCn);
        Assert.Equal("zh-CN", adapter.Override);

        service.ApplyLanguage(AppLanguagePreference.System);
        Assert.Null(adapter.Override);
    }

    [Fact]
    public async Task LanguageSwitch_AppliesLiveAndNeverRequestsRestart()
    {
        using var temp = new TempDirectory();
        var store = new JsonAppearanceSettingsStore(temp.Path);
        var adapter = new FakeLanguageAdapter();
        var language = new LanguageService(adapter);
        bool restartRequested = false;
        int changed = 0;
        language.LanguageChanged += () => changed++;

        var vm = new AppearanceSettingsViewModel(
            store,
            new AppearanceService(),
            language,
            requestRestart: () =>
            {
                restartRequested = true;
                return true;
            },
            confirmRestartAsync: () => throw new InvalidOperationException("Restart dialog must not open"));
        await vm.InitializeAsync();

        vm.SelectedLanguage = vm.LanguageOptions.First(o => o.Language == AppLanguagePreference.JaJp);
        for (int attempt = 0; attempt < 30 && vm.IsLanguageBusy; attempt++)
            await Task.Delay(15);
        await Task.Delay(60);

        Assert.False(restartRequested);
        Assert.Equal("ja-JP", adapter.Override);
        Assert.Equal(1, changed);
        Assert.False(vm.HasStatus);
        Assert.Equal(nameof(AppLanguagePreference.JaJp),
            (await store.LoadAsync(CancellationToken.None)).Language);
        Assert.Equal(AppLanguagePreference.JaJp, vm.SelectedLanguage.Language);
    }

    [Fact]
    public void LanguageService_OnlyNotifiesOnActualPreferenceChanges()
    {
        var service = new LanguageService(new FakeLanguageAdapter());
        int notifications = 0;
        service.LanguageChanged += () => notifications++;

        service.ApplyLanguage(AppLanguagePreference.EnUs);
        service.ApplyLanguage(AppLanguagePreference.EnUs);
        service.ApplyLanguage(AppLanguagePreference.System);

        Assert.Equal(2, notifications);
    }
}
