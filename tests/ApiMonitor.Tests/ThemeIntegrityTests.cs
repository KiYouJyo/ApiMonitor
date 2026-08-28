using System.Text.RegularExpressions;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 主题统一测试（文本级解析，避免 XAML 命名空间与 XML 解析冲突）：
///   1. App.xaml 的 Light/Dark/HighContrast 字典定义完整语义资源；
///   2. 外壳、侧栏与卡片使用分层语义资源；
///   3. Shell XAML 中不存在固定 Black/Gray 背景；
///   4. NavigationView Pane 覆盖为 AppPaneBackgroundBrush；
///   5. 页面根元素不设置独立固定灰色背景；
///   6. 主题协调器同步标题栏。
/// </summary>
public sealed class ThemeIntegrityTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ApiMonitor.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录。");
    }

    private static string ReadText(string relativePath)
    {
        string path = Path.Combine(RepoRoot, relativePath);
        Assert.True(File.Exists(path), $"缺少文件：{path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void ThemeDictionaries_DefineAllSemanticResources()
    {
        string app = ReadText("App.xaml");
        string[] required =
        {
            "AppShellBackgroundBrush",
            "AppPaneBackgroundBrush",
            "AppCardBackgroundBrush",
            "AppSubtleSurfaceBrush",
            "AppCardBorderBrush",
            "AppPrimaryTextBrush",
            "AppSecondaryTextBrush",
            "AppNavigationSelectedBrush",
        };

        foreach (var theme in new[] { "Light", "Dark", "HighContrast" })
        {
            var match = Regex.Match(app, $@"ResourceDictionary x:Key=""{theme}"">(.*?)</ResourceDictionary>", RegexOptions.Singleline);
            Assert.True(match.Success, $"缺少 {theme} 主题字典。");
            string block = match.Groups[1].Value;
            foreach (var key in required)
            {
                Assert.True(
                    Regex.IsMatch(block, $@"x:Key=""{key}"""),
                    $"{theme} 字典缺少 {key}");
            }
        }
    }

    [Fact]
    public void ShellPaneAndCardBackground_UseLayeredSemanticResources()
    {
        string app = ReadText("App.xaml");
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var match = Regex.Match(app, $@"ResourceDictionary x:Key=""{theme}"">(.*?)</ResourceDictionary>", RegexOptions.Singleline);
            Assert.True(match.Success);
            string block = match.Groups[1].Value;
            string shell = Regex.Match(block, @"x:Key=""AppShellBackgroundBrush""[^>]*Color=""([^""]+)""").Groups[1].Value;
            string pane = Regex.Match(block, @"x:Key=""AppPaneBackgroundBrush""[^>]*Color=""([^""]+)""").Groups[1].Value;
            string card = Regex.Match(block, @"x:Key=""AppCardBackgroundBrush""[^>]*Color=""([^""]+)""").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(shell), $"{theme} 缺少外壳颜色。");
            Assert.False(string.IsNullOrEmpty(pane), $"{theme} 缺少侧栏颜色。");
            Assert.False(string.IsNullOrEmpty(card), $"{theme} 缺少卡片颜色。");
            Assert.NotEqual(shell, card);
            Assert.NotEqual(pane, card);
        }
    }

    [Fact]
    public void ShellXaml_HasNoHardcodedBlackGrayBackground()
    {
        foreach (var f in new[] { "Views/MainPage.xaml", "MainWindow.xaml" })
        {
            string content = ReadText(f);
            var matches = Regex.Matches(
                content,
                @"Background=""(?:#(?:000000|111111|202020|2B2B2B|333333|808080)|Black|Gray|DarkGray|LightGray)""");
            Assert.True(matches.Count == 0, $"{f} 含硬编码背景色：{string.Join(", ", matches.Select(m => m.Value))}");
        }
    }

    [Fact]
    public void MainPage_OverridesNavigationPaneBackground_ToPaneBrush()
    {
        string page = ReadText("Views/MainPage.xaml");
        var overrides = Regex.Matches(
            page,
            @"StaticResource x:Key=""NavigationViewDefaultPaneBackground"" ResourceKey=""([^""]+)""");
        Assert.True(overrides.Count >= 2, "MainPage 应覆盖 NavigationViewDefaultPaneBackground（Light/Dark）。");
        foreach (Match m in overrides)
        {
            Assert.Equal("AppPaneBackgroundBrush", m.Groups[1].Value);
        }
    }

    [Fact]
    public void PageRoots_HaveNoIndependentFixedBackground()
    {
        foreach (var f in new[] { "HomePage", "InsightsPage", "SettingsPage", "AboutPage" })
        {
            string content = ReadText("Views/" + f + ".xaml");
            var rootMatch = Regex.Match(
                content,
                @"<UserControl[\s\S]*?<(?<el>Grid|ScrollViewer)[^>]*Background=""(?<bg>[^""]+)""");
            Assert.False(rootMatch.Success, $"{f}.xaml 根元素设置了固定 Background={rootMatch.Groups["bg"].Value}");
        }
    }

    [Fact]
    public void MainWindow_UsesWindowThemeCoordinator()
    {
        string root = ReadText("Services/CompositionRoot.cs");
        Assert.Contains("WindowThemeCoordinator", root);
        Assert.Contains("RegisterWindow", root);
        string coordinator = ReadText("Services/WindowThemeCoordinator.cs");
        Assert.Contains("SyncTitleBar", coordinator);
        Assert.Contains("AppWindowTitleBar", coordinator);
    }

    [Fact]
    public void ThemeCoordinator_HandlesHighContrastAndTitleBarColors()
    {
        string coordinator = ReadText("Services/WindowThemeCoordinator.cs");
        Assert.Contains("AccessibilitySettings", coordinator);
        Assert.Contains("ButtonHoverBackgroundColor", coordinator);
        Assert.Contains("ButtonPressedBackgroundColor", coordinator);
        Assert.Contains("ButtonForegroundColor", coordinator);
    }
}
