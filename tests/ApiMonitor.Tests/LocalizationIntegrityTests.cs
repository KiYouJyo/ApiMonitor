using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v0.6.0 本地化完整性测试：
///   1. XAML 中所有 loc:Loc.Key 引用的资源键在三语 resw 中必须存在；
///   2. 键集合三语完全一致，且值非空；
///   3. 关键控件（按钮/标题/ComboBox 项）的资源键有非空翻译；
///   4. Loc 附加属性的占位机制不会把已知键误判为缺失。
/// </summary>
public sealed class LocalizationIntegrityTests
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

    private static Dictionary<string, Dictionary<string, string>> LoadAllLangs()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        foreach (var lang in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            string path = Path.Combine(RepoRoot, "Strings", lang, "Resources.resw");
            Assert.True(File.Exists(path), $"缺少资源文件：{path}");
            var doc = XDocument.Load(path);
            var dict = doc.Root!
                .Elements("data")
                .Where(e => e.Attribute("name") is not null)
                .ToDictionary(e => e.Attribute("name")!.Value, e => e.Element("value")?.Value ?? string.Empty);
            result[lang] = dict;
        }

        return result;
    }

    /// <summary>收集所有 XAML 中的 loc:Loc.Key 引用。</summary>
    private static HashSet<string> CollectLocKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(RepoRoot, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.Combine("bin")) || file.Contains(Path.Combine("obj")))
            {
                continue;
            }

            string content = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(content, @"loc:Loc\.Key=""([^""]+)"""))
            {
                keys.Add(m.Groups[1].Value);
            }
        }

        return keys;
    }

    [Fact]
    public void AllLocKeys_ExistInAllThreeLanguages()
    {
        var langs = LoadAllLangs();
        var locKeys = CollectLocKeys();
        Assert.True(locKeys.Count > 0, "XAML 中应存在 loc:Loc.Key 引用。");

        foreach (var key in locKeys)
        {
            foreach (var (lang, dict) in langs)
            {
                bool found = dict.ContainsKey(key)
                    || dict.ContainsKey(key + ".Text")
                    || dict.ContainsKey(key + ".Content")
                    || dict.ContainsKey(key + ".Header")
                    || dict.ContainsKey(key + ".Title")
                    || dict.ContainsKey(key + ".Message");
                Assert.True(found, $"{lang} 缺少 Loc.Key 对应的资源：{key}");
            }
        }
    }

    [Fact]
    public void ThreeLanguages_HaveIdenticalKeySets()
    {
        var langs = LoadAllLangs();
        var zh = langs["zh-CN"].Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        foreach (var lang in new[] { "en-US", "ja-JP" })
        {
            var other = langs[lang].Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(zh, other);
        }
    }

    [Fact]
    public void AllValues_NonEmpty()
    {
        var langs = LoadAllLangs();
        foreach (var (lang, dict) in langs)
        {
            foreach (var (key, value) in dict)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{lang} 的 {key} 值为空。");
            }
        }
    }

    /// <summary>关键导航与按钮键在三种语言下都有非空值。</summary>
    [Fact]
    public void CriticalKeys_HaveNonEmptyValues()
    {
        var langs = LoadAllLangs();
        string[] critical =
        {
            "Nav.Home.Content", "Nav.Insights.Content", "Nav.Settings.Content", "Nav.About.Content",
            "Home.AddAccount.Content", "Home.RefreshAll.Content", "Home.AccountOverview.Text",
            "Home.FilterProvider.Text", "Home.FilterStatus.Text", "Home.Refresh.Content",
            "Home.Edit.Content", "Home.ViewTrends.Content", "Home.CopyKey.Content",
            "Home.History.Content", "Home.Delete.Content",
            "Settings.Title.Text", "Settings.TraySection.Text", "Settings.NotificationSection.Text",
            "Settings.AppearanceSection.Text", "Settings.DataSection.Text",
            "Settings.ExportBackup.Content", "Settings.ImportBackup.Content", "Settings.OpenDataFolder.Content",
            "Settings.Theme.Text", "Settings.Language.Text",
            "Insights.Title.Text", "Insights.Subtitle.Text", "Insights.Account.Text",
            "Insights.Metric.Text", "Insights.TimeRange.Text", "Insights.ExportCsv.Content",
            "About.Title.Text", "About.CheckUpdates.Content", "About.CopyDiagnostics.Content",
            "About.OpenDataFolder.Content",
        };

        foreach (var key in critical)
        {
            foreach (var (lang, dict) in langs)
            {
                // 支持无后缀与各属性后缀。
                var candidates = new[] { key }
                    .Concat(new[] { ".Text", ".Content", ".Header", ".Title" }.Select(s => key + s));
                string? value = candidates.Select(c => dict.TryGetValue(c, out var v) ? v : null)
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                Assert.True(value is not null, $"{lang} 关键键缺失或为空：{key}");
            }
        }
    }

    /// <summary>
    /// 验证无 PRI“既是资源又是范围”冲突：不存在某个键 k，同时存在叶子键 k
    /// 和以 k 为前缀的属性键（如 k.Text / k.Content）。
    /// 示例冲突：Home.PrivacyMessage（叶子）与 Home.PrivacyMessage.Message（属性）。
    /// </summary>
    [Fact]
    public void NoKeyIsBothLeafAndParentOfPropertySuffix()
    {
        var langs = LoadAllLangs();
        var keys = langs["zh-CN"].Keys;
        var leafSet = new HashSet<string>(keys, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            // 若 key 形如 X.Text/X.Content/X.Header/X.Title/X.Message，检查 X 是否也是叶子键。
            foreach (string suffix in new[] { ".Text", ".Content", ".Header", ".Title", ".Message" })
            {
                if (key.EndsWith(suffix, StringComparison.Ordinal))
                {
                    string parent = key[..^suffix.Length];
                    Assert.False(
                        leafSet.Contains(parent),
                        $"PRI 冲突：{parent} 既是资源又是 {key} 的范围。");
                }
            }
        }
    }

    /// <summary>ComboBox 选项相关的资源（如筛选标签）在三种语言下均有值。</summary>
    [Fact]
    public void ComboBoxRelatedKeys_HaveValues()
    {
        var langs = LoadAllLangs();
        string[] keys =
        {
            "Home.FilterAllProviders", "Home.FilterAllStatus",
            "Home.StatusNormal", "Home.StatusLow", "Home.StatusUnknown", "Home.StatusFailed",
        };
        foreach (var key in keys)
        {
            foreach (var (lang, dict) in langs)
            {
                Assert.True(dict.ContainsKey(key) && !string.IsNullOrWhiteSpace(dict[key]),
                    $"{lang} 的 ComboBox 相关键 {key} 缺失或为空。");
            }
        }
    }
}
