using System.Xml.Linq;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v0.7.0 悬浮窗/主页内容级验收测试：
///   1. 主页 XAML 不再包含旧副标题绑定与“打开紧凑窗口”按钮；
///   2. 账户卡片保留“设为悬浮窗”入口；
///   3. 三语资源不再包含任何 Compact.* / OpenCompactWindow 键；
///   4. 三语资源包含完整的 Floating.* 与托盘/卡片悬浮窗键。
/// </summary>
public sealed class FloatingWindowContentTests
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

    [Fact]
    public void HomePage_NoLongerShowsOldSubtitleOrCompactWindowButton()
    {
        string home = File.ReadAllText(Path.Combine(RepoRoot, "Views", "HomePage.xaml"));

        Assert.DoesNotContain("Home.OpenCompactWindow", home);
        Assert.DoesNotContain("OpenCompactWindowCommand", home);
        Assert.Contains("x:Name=\"PageTitleText\"", home);
        Assert.Contains("x:Name=\"HomeActionBar\"", home);
        Assert.Contains("x:Name=\"HomeStatusInfoBar\"", home);
        Assert.Contains("x:Name=\"AccountOverviewBorder\"", home);
        Assert.True(home.IndexOf("PageTitleText", StringComparison.Ordinal) < home.IndexOf("HomeActionBar", StringComparison.Ordinal));
        Assert.True(home.IndexOf("HomeActionBar", StringComparison.Ordinal) < home.IndexOf("HomeStatusInfoBar", StringComparison.Ordinal));
        Assert.True(home.IndexOf("HomeStatusInfoBar", StringComparison.Ordinal) < home.IndexOf("AccountOverviewBorder", StringComparison.Ordinal));
        Assert.DoesNotContain("{Binding SubtitleText}", home);
        Assert.Contains("Home.SetAsFloatingWindow", home);
        Assert.Contains("SetAsFloatingWindowCommand", home);
    }

    [Fact]
    public void Resources_NoLongerContainCompactWindowKeys()
    {
        foreach (var lang in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            var keys = LoadKeys(lang);
            Assert.DoesNotContain(keys, k => k.StartsWith("Compact.", StringComparison.Ordinal));
            Assert.DoesNotContain("Tray.OpenCompactWindow", keys);
            Assert.DoesNotContain("Home.OpenCompactWindow.Content", keys);
            Assert.DoesNotContain("Home.Subtitle", keys);
            Assert.DoesNotContain("Home.SubtitleFormat", keys);
        }
    }

    [Fact]
    public void FloatingWindow_UsesCompactSquareCardAndShortStatusResources()
    {
        string xaml = File.ReadAllText(Path.Combine(RepoRoot, "Views", "FloatingBalanceWindow.xaml"));
        Assert.Contains("CornerRadius=\"12\"", xaml);
        Assert.Contains("AppCardBackgroundBrush", xaml);
        Assert.Contains("Text=\"{Binding BalanceText}\"", xaml);
        Assert.Contains("Text=\"{Binding UnitText}\"", xaml);
        Assert.DoesNotContain("LastUpdatedText", xaml);

        foreach (string language in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            string resources = File.ReadAllText(Path.Combine(RepoRoot, "Strings", language, "Resources.resw"));
            foreach (string key in new[] { "Floating.StatusNormal", "Floating.StatusLow", "Floating.StatusFailed", "Floating.StatusUnknown" })
            {
                Assert.Contains($"name=\"{key}\"", resources);
            }
        }
    }

    [Fact]
    public void MainWindow_ExplicitlyUsesCustomIcoForWindowChrome()
    {
        string code = File.ReadAllText(Path.Combine(RepoRoot, "MainWindow.xaml.cs"));
        Assert.Contains("AppWindow.SetIcon", code);
        Assert.Contains("ApiMonitor.ico", code);
        Assert.True(File.Exists(Path.Combine(RepoRoot, "Assets", "ApiMonitor.ico")));
    }

    [Fact]
    public void Resources_ContainCompleteFloatingWindowKeys()
    {
        string[] required =
        {
            "Floating.NotQueried",
            "Floating.NoAccounts",
            "Floating.NoSelectedAccount",
            "Floating.Unknown",
            "Floating.QueryFailed",
            "Floating.LastUpdatedFormat",
            "Tray.OpenFloatingWindow",
            "Home.SetAsFloatingWindow.Content",
        };

        foreach (var lang in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            var keys = LoadKeys(lang);
            foreach (var key in required)
            {
                Assert.Contains(key, keys);
            }
        }
    }

    private static HashSet<string> LoadKeys(string lang)
    {
        string path = Path.Combine(RepoRoot, "Strings", lang, "Resources.resw");
        Assert.True(File.Exists(path), $"缺少资源文件：{path}");
        var doc = XDocument.Load(path);
        return doc.Root!
            .Elements("data")
            .Where(e => e.Attribute("name") is not null)
            .Select(e => e.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);
    }
}
