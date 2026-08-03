using System.Runtime.CompilerServices;
using System.Xml.Linq;
using ApiMonitor.Services;

namespace ApiMonitor.Tests;

/// <summary>
/// 测试程序集初始化：用 zh-CN 资源键填充 L10n 映射，
/// 使 VM 在测试中的本地化文本与中文界面一致（测试断言基于中文）。
/// </summary>
public static class TestL10nInitializer
{
    private static bool _initialized;

    [ModuleInitializer]
    public static void Init()
    {
        if (_initialized)
        {
            return;
        }

        string repoRoot = FindRepoRoot();
        string resw = Path.Combine(repoRoot, "Strings", "zh-CN", "Resources.resw");
        if (File.Exists(resw))
        {
            var doc = XDocument.Load(resw);
            var map = doc.Root!
                .Elements("data")
                .Where(e => e.Attribute("name") is not null)
                .ToDictionary(
                    e => e.Attribute("name")!.Value,
                    e => e.Element("value")?.Value ?? string.Empty);
            L10n.InitializeWithMap(map);
        }
        else
        {
            // 无资源文件时用空映射（测试将显示 [Missing: key]，便于定位）。
            L10n.InitializeWithMap(new Dictionary<string, string>());
        }

        _initialized = true;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ApiMonitor.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录。");
    }
}
