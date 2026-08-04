using System.Xml.Linq;
using ApiMonitor.Services;
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
        Assert.Contains("x:Name=\"SingleRootSurface\"", xaml);
        Assert.Contains("Width=\"168\"", xaml);
        Assert.Contains("Height=\"168\"", xaml);
        Assert.Contains("CornerRadius=\"18\"", xaml);
        Assert.Contains("FloatingSurfaceBackgroundBrush", xaml);
        Assert.Contains("Text=\"{Binding BalanceText}\"", xaml);
        Assert.Contains("Text=\"{Binding UnitText}\"", xaml);
        Assert.Contains("FontSize=\"{Binding AmountFontSize}\"", xaml);
        Assert.DoesNotContain("LastUpdatedText", xaml);
        Assert.Equal(1, CountOccurrences(xaml, "Background=\"{ThemeResource FloatingSurfaceBackgroundBrush}\""));
        Assert.DoesNotContain("AppCardBackgroundBrush", xaml);
        Assert.DoesNotContain("BorderBrush=", xaml);
        Assert.DoesNotContain("BorderThickness=", xaml);
        Assert.Equal(1, CountOccurrences(xaml, "CornerRadius=\""));
        Assert.DoesNotContain("Shadow", xaml);
        Assert.DoesNotContain("Margin=\"-", xaml);
        Assert.DoesNotContain("Translation=", xaml);
        Assert.DoesNotContain("RenderTransform=", xaml);
        Assert.DoesNotContain("Clip=", xaml);

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
    public void FloatingWindow_IsSingleSurfaceFixedSizeToolWindowAndUsesNativeCaptionDrag()
    {
        string code = File.ReadAllText(Path.Combine(RepoRoot, "Views", "FloatingBalanceWindow.xaml.cs"));
        string native = File.ReadAllText(Path.Combine(RepoRoot, "Services", "NativeMethods.cs"));
        string xaml = File.ReadAllText(Path.Combine(RepoRoot, "Views", "FloatingBalanceWindow.xaml"));

        Assert.Contains("SetBorderAndTitleBar", code);
        Assert.Contains("IsResizable = false", code);
        Assert.Contains("IsMaximizable = false", code);
        Assert.Contains("IsMinimizable = false", code);
        Assert.Contains("WS_POPUP", code);
        Assert.Contains("WS_EX_TOOLWINDOW", code);
        Assert.Contains("WS_EX_APPWINDOW", code);
        Assert.Contains("InputNonClientPointerSource", code);
        Assert.Contains("NonClientRegionKind.Caption", code);
        Assert.Contains("SetRegionRects", code);
        Assert.DoesNotContain("PointerPressed", xaml);
        Assert.DoesNotContain("PointerMoved", code);
        Assert.DoesNotContain("PointerReleased", code);
        Assert.DoesNotContain("CapturePointer", code);
        Assert.DoesNotContain("AppWindow.Move", code);
        Assert.DoesNotContain("MoveAndResize", code);
        Assert.DoesNotContain("WM_NCLBUTTONDOWN", code);
        Assert.DoesNotContain("ApplyRoundedWindowRegion", code);
        Assert.DoesNotContain("SetWindowRgn", native);
        Assert.DoesNotContain("CreateRoundRectRgn", native);
        Assert.DoesNotContain("ReleaseCapture", native);
        Assert.DoesNotContain("SendMessageW", native);
        Assert.Contains("SetWindowPos", native);
        Assert.Contains("ApplyWindowBounds", code);
        Assert.Contains("OnAppWindowChanged", code);
        Assert.Contains("600", code);
        Assert.Contains("_positionSaveCount", code);
        Assert.Equal(FloatingWindowDefaults.FixedSize, FloatingWindowDefaults.DefaultWidth);
        Assert.Equal(FloatingWindowDefaults.FixedSize, FloatingWindowDefaults.DefaultHeight);
        Assert.Equal(FloatingWindowDefaults.FixedSize, FloatingWindowDefaults.MinWidth);
        Assert.Equal(FloatingWindowDefaults.FixedSize, FloatingWindowDefaults.MaxWidth);
    }

    private static int CountOccurrences(string value, string needle)
    {
        int count = 0;
        for (int offset = 0; (offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0; offset += needle.Length)
        {
            count++;
        }

        return count;
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
            "Tray.CloseFloatingWindow",
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
