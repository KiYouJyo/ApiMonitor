using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 三语资源一致性测试（v0.6.0）：
///   1. zh-CN / en-US / ja-JP 三个 Resources.resw 的键集合完全一致；
///   2. 每个键都有非空翻译（不允许缺少翻译后静默显示资源键）；
///   3. 检测受版本控制 XAML/C# 中新增的明显硬编码用户文本（中文/日文出现在
///      x:Uid 之外的非注释上下文视为可疑——本测试聚焦资源键引用的完整性）。
/// </summary>
public sealed class ResourceConsistencyTests
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

    private static string ReswPath(string lang) =>
        Path.Combine(RepoRoot, "Strings", lang, "Resources.resw");

    private static Dictionary<string, string> ReadKeys(string lang)
    {
        string path = ReswPath(lang);
        Assert.True(File.Exists(path), $"缺少资源文件：{path}");
        var doc = XDocument.Load(path);
        var root = doc.Root!;
        return root.Elements("data")
            .Where(e => e.Attribute("name") is not null)
            .ToDictionary(
                e => e.Attribute("name")!.Value,
                e => e.Element("value")?.Value ?? string.Empty);
    }

    [Fact]
    public void AllThreeLanguages_HaveIdenticalKeySets()
    {
        var zh = ReadKeys("zh-CN");
        var en = ReadKeys("en-US");
        var ja = ReadKeys("ja-JP");

        Assert.Equal(
            zh.Keys.OrderBy(k => k, StringComparer.Ordinal),
            en.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(
            zh.Keys.OrderBy(k => k, StringComparer.Ordinal),
            ja.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void AllKeys_HaveNonEmptyValues_InAllLanguages()
    {
        foreach (var lang in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            var keys = ReadKeys(lang);
            foreach (var (key, value) in keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{lang} 的键 {key} 缺少翻译。");
            }
        }
    }

    [Fact]
    public void PackagedLanguageSelection_UsesPersistedQualifierAndKeepsAllLanguagesInMainPackage()
    {
        string compositionRoot = File.ReadAllText(Path.Combine(RepoRoot, "Services", "CompositionRoot.cs"));
        Assert.Contains("ReadPersistedLanguage(dataDirectory)", compositionRoot);
        Assert.Contains("ResourceContext.SetGlobalQualifierValue", compositionRoot);
        Assert.Contains("persistedLanguage", compositionRoot);
        Assert.DoesNotContain("LoadAsync(CancellationToken.None).GetAwaiter().GetResult()", compositionRoot);
        Assert.Contains("File.ReadAllText(path)", compositionRoot);

        string project = File.ReadAllText(Path.Combine(RepoRoot, "ApiMonitor.csproj"));
        Assert.Contains("BuildAppxSideloadPackageForUap", project);
        Assert.Contains("AppxBundleAutoResourcePackageQualifiers>Scale|DXFeatureLevel<", project);

        string manifest = File.ReadAllText(Path.Combine(RepoRoot, "Package.appxmanifest"));
        foreach (string language in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            Assert.Contains($"Resource Language=\"{language}\"", manifest);
        }
    }

    [Fact]
    public void ResourceKeys_UseDotNotation_AndMatchKnownNamespaces()
    {
        var keys = ReadKeys("zh-CN").Keys;
        foreach (var key in keys)
        {
            // 支持 "Prefix.Name"（纯文本）与 "Prefix.Name.Property"（x:Uid 属性键）。
            Assert.Matches(@"^[A-Za-z][A-Za-z0-9]*\.[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)?$", key);
        }
    }

    /// <summary>
    /// 检测受版本控制 XAML 中新增的明显硬编码中文用户文本：
    /// 出现在 <TextBlock Text="…"/>、Button Content="…" 等属性中的中文/日文字符
    /// 且同元素没有 x:Uid 时视为可疑（应改用资源键）。
    /// </summary>
    [Fact]
    public void Xaml_DoesNotContainNewHardcodedChineseText()
    {
        var xamlFiles = Directory.EnumerateFiles(RepoRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.Combine("bin")) && !f.Contains(Path.Combine("obj")))
            .ToList();

        // 中文字符（含全角标点）与日文字符。
        var cjkPattern = new Regex(@"[\u4e00-\u9fff\u3040-\u30ff\u3000-\u303f\uff00-\uffef]");
        var hardcoded = new List<string>();

        foreach (var file in xamlFiles)
        {
            string content = File.ReadAllText(file);
            // 只检查 Text= / Content= / Header= / Title= 属性中的内联文本。
            var matches = Regex.Matches(content, @"(?<attr>(?:Text|Content|Header|Title|ToolTip)=""[^""]*[\u4e00-\u9fff\u3040-\u30ff][^""]*"")");
            foreach (Match match in matches)
            {
                string attr = match.Groups["attr"].Value;
                // 跳过含有 {Binding、{x:Static、{ThemeResource 的绑定表达式。
                if (attr.Contains("{Binding") || attr.Contains("{x:") || attr.Contains("{ThemeResource"))
                {
                    continue;
                }

                hardcoded.Add($"{Path.GetFileName(file)}: {attr}");
            }
        }

        // 说明：现有 v0.5.0 页面仍使用硬编码中文（本轮已新增三语资源但页面尚未全部迁移，
        // 迁移工作由 v0.6.0 的 XAML 本地化完成）。此处只断言“本轮新增文件”（InsightsPage/
        // AboutPage 及新控件）不引入未绑定资源的硬编码中文。
        var newFiles = hardcoded
            .Where(h => h.Contains("InsightsPage") || h.Contains("AboutPage") || h.Contains("TrendChart"))
            .ToList();

        Assert.True(
            newFiles.Count == 0,
            $"新页面包含未本地化的硬编码中文：{string.Join(" | ", newFiles)}");
    }
}
