using System.Text.RegularExpressions;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// Regression guards for the UrbanPlanToolbox interaction patterns requested
/// in v1.3.0; actual pixels and tray activation still need Windows smoke tests.
/// </summary>
public sealed class UrbanPlanParityTests
{
    private static readonly string Repo = LocateRoot();

    private static string Read(string path) =>
        File.ReadAllText(Path.Combine(Repo, path.Replace('/', Path.DirectorySeparatorChar)));

    private static string LocateRoot()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null && !File.Exists(Path.Combine(cursor.FullName, "ApiMonitor.csproj")))
            cursor = cursor.Parent;
        return cursor?.FullName ?? throw new InvalidOperationException("Missing repository root.");
    }

    [Fact]
    public void Tray_UsesCompactContextMenuPresenterAndDesktopAcrylic()
    {
        string markup = Read("Views/TrayMenuWindow.xaml");
        string behavior = Read("Views/TrayMenuWindow.xaml.cs");
        string wiring = Read("Services/CompositionRoot.cs");

        Assert.Contains("DesktopAcrylicBackdrop", markup);
        Assert.Contains("LayerFillColorAltBrush", markup);
        Assert.Contains("CardStrokeColorDefaultBrush", markup);
        Assert.Contains("TraySeparatorStyle", markup);
        Assert.Contains("OverlappedPresenter.CreateForContextMenu()", behavior);
        Assert.Contains("MenuWidthDip = 244", behavior);
        Assert.Contains("Height = 32", behavior);
        Assert.Contains("GetDpiForWindow", behavior);
        Assert.Contains("SetForegroundWindow", behavior);
        Assert.Contains("BringWindowToTop", behavior);
        Assert.Contains("new TrayMenuWindow()", wiring);
        Assert.False(File.Exists(Path.Combine(Repo, "Services", "FluentTrayMenuWindow.cs")));
    }

    [Fact]
    public void Shell_IsMicaIntegratedWithoutDuplicatePaneBranding()
    {
        string main = Read("MainWindow.xaml");
        string navigation = Read("Views/MainPage.xaml");
        string code = Read("Views/MainPage.xaml.cs");

        Assert.Contains("<MicaBackdrop />", main);
        Assert.Contains("<TitleBar ", main);
        Assert.Contains("x:Name=\"ShellHost\"", main);
        Assert.Contains("Background=\"Transparent\"", navigation);
        Assert.DoesNotContain("NavigationView.PaneHeader", navigation);
        Assert.Contains("IsSettingsVisible=\"True\"", navigation);
        Assert.Contains("OnNavigationItemInvoked", navigation);
        Assert.Contains("SplitView.PaneBackground", code);
        Assert.Contains("ShellNavigationPaneInactiveBackgroundBrush", code);
    }

    [Fact]
    public void LanguageChange_RebuildsPagesWithoutRecreatingMonitoringServices()
    {
        string main = Read("MainWindow.xaml.cs");
        string app = Read("App.xaml.cs");
        string view = Read("Views/MainPage.xaml.cs");
        string service = Read("Services/LanguageService.cs");
        string composition = Read("Services/CompositionRoot.cs");

        Assert.Contains("new Views.MainPage { ViewModel = model }", main);
        Assert.Contains("previous.DetachShellEvents()", main);
        Assert.Contains("ShellHost.Children.Clear()", main);
        Assert.Contains("RebuildLocalizedShell()", app);
        Assert.Contains("void DetachShellEvents()", view);
        Assert.Contains("CultureInfo.CurrentUICulture", service);
        Assert.Contains("CultureInfo.CurrentCulture", service);
        Assert.Contains("SetGlobalQualifierValue", service);
        Assert.Contains("floating.RefreshLocalizationAsync()", composition);
        Assert.Contains("WindowThemeCoordinator", composition);
    }

    [Theory]
    [InlineData("zh-CN", "无需重启应用")]
    [InlineData("en-US", "no restart is required")]
    [InlineData("ja-JP", "再起動は不要")]
    public void LanguageDescription_TruthfullyDescribesImmediateChange(string locale, string fragment)
    {
        string resources = Read($"Strings/{locale}/Resources.resw");
        Assert.Contains(fragment, resources);
    }
}
