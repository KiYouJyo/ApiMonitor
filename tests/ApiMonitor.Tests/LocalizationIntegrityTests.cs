using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>三语资源、附加本地化属性与核心壳层硬编码完整性。</summary>
public sealed class LocalizationIntegrityTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string[] Languages = { "zh-CN", "en-US", "ja-JP" };
    private static readonly string[] PropertySuffixes =
    {
        ".Text", ".Content", ".Header", ".Title", ".Message", ".Description",
        ".PlaceholderText", ".ToolTip", ".AutomationName", ".OnContent", ".OffContent",
        ".PrimaryButtonText", ".SecondaryButtonText", ".CloseButtonText",
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ApiMonitor.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录。");
    }

    private static Dictionary<string, Dictionary<string, string>> LoadAllLangs()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        foreach (string language in Languages)
        {
            string directory = Path.Combine(RepoRoot, "Strings", language);
            Assert.True(Directory.Exists(directory), $"缺少语言目录：{directory}");
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in Directory.EnumerateFiles(directory, "*.resw", SearchOption.TopDirectoryOnly))
            {
                var doc = XDocument.Load(path);
                foreach (var element in doc.Root!.Elements("data").Where(e => e.Attribute("name") is not null))
                {
                    string key = element.Attribute("name")!.Value;
                    string value = element.Element("value")?.Value ?? string.Empty;
                    Assert.True(dict.TryAdd(key, value), $"{language} 存在重复资源键：{key}");
                }
            }
            result[language] = dict;
        }
        return result;
    }

    private static HashSet<string> CollectLocKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var pattern = new Regex(@"loc:Loc\.(?:Key|PlaceholderKey|ToolTipKey|AutomationNameKey|OnContentKey|OffContentKey)=""([^""]+)""");
        foreach (string file in Directory.EnumerateFiles(RepoRoot, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                || file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            string content = File.ReadAllText(file);
            foreach (Match match in pattern.Matches(content)) keys.Add(match.Groups[1].Value);
        }
        return keys;
    }

    private static bool ContainsResource(IReadOnlyDictionary<string, string> dict, string key) =>
        new[] { key }.Concat(PropertySuffixes.Select(s => key + s)).Any(dict.ContainsKey);

    [Fact]
    public void AllLocKeys_ExistInAllThreeLanguages()
    {
        var langs = LoadAllLangs();
        var keys = CollectLocKeys();
        Assert.NotEmpty(keys);
        foreach (string key in keys)
            foreach (string language in Languages)
                Assert.True(ContainsResource(langs[language], key), $"{language} 缺少 Loc 资源：{key}");
    }

    [Fact]
    public void ThreeLanguages_HaveIdenticalCombinedKeySets()
    {
        var langs = LoadAllLangs();
        var baseline = langs["zh-CN"].Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(baseline, langs["en-US"].Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal(baseline, langs["ja-JP"].Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void AllResourceValues_AreNonEmpty()
    {
        foreach (var (language, dict) in LoadAllLangs())
            foreach (var (key, value) in dict)
                Assert.False(string.IsNullOrWhiteSpace(value), $"{language} 的 {key} 为空。");
    }

    [Fact]
    public void NoKeyIsBothLeafAndPropertyParent()
    {
        foreach (var (language, dict) in LoadAllLangs())
        {
            var keys = new HashSet<string>(dict.Keys, StringComparer.Ordinal);
            foreach (string key in keys)
                foreach (string suffix in PropertySuffixes)
                {
                    if (!key.EndsWith(suffix, StringComparison.Ordinal)) continue;
                    string parent = key[..^suffix.Length];
                    Assert.False(keys.Contains(parent), $"{language} PRI 冲突：{parent} 与 {key}");
                }
        }
    }

    [Fact]
    public void CoreShellPages_HaveNoHardcodedCjkVisibleText()
    {
        string[] files = { "Views/MainPage.xaml", "Views/SettingsPage.xaml", "Views/AboutPage.xaml" };
        var visibleAttribute = new Regex(@"(?:Text|Content|Header|PlaceholderText|OnContent|OffContent|AutomationProperties\.Name|ToolTipService\.ToolTip)=""([^""]*)""");
        var cjk = new Regex("[\\u3040-\\u30ff\\u3400-\\u9fff]");
        foreach (string relative in files)
        {
            string content = File.ReadAllText(Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            foreach (Match match in visibleAttribute.Matches(content))
            {
                string value = match.Groups[1].Value;
                if (value.StartsWith("{", StringComparison.Ordinal)) continue;
                Assert.False(cjk.IsMatch(value), $"{relative} 存在可见硬编码文本：{value}");
            }
        }
    }

    [Fact]
    public void CriticalModernKeys_ArePresentInEveryLanguage()
    {
        string[] keys =
        {
            "Settings.WebDavSection.Text", "Settings.WebDavEndpoint.Header", "Settings.WebDavPassword.Header",
            "Settings.WebDavBackupNow.Content", "Settings.WebDavRestoreLatest.Content",
            "About.InstallUpdate.Content", "Update.Installing", "Update.NoPackageAsset",
        };
        foreach (var (language, dict) in LoadAllLangs())
            foreach (string key in keys)
                Assert.True(dict.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value), $"{language} 缺少 {key}");
    }
}
