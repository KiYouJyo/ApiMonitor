using System.Reflection;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace ApiMonitor.Services;

/// <summary>
/// v0.6.0：统一元数据服务。DisplayVersion 与 PackageVersion 明确分离：
///   - DisplayVersion 来自 AssemblyInformationalVersion（如 1.0.0），用于界面/关于页；
///   - PackageVersion 来自 MSIX Package（如 1.0.0.1 / 1.0.0.0），用于升级判断；
/// v1.0.0：包身份（Name/Publisher/PackageFamily）优先读取安装包实际值
/// （Package.Current），未打包时回退到构建渠道对应的期望值。
/// 打包运行时通过包信息读取；未打包调试运行时提供安全回退，绝不因
/// Package.Current 不可用而崩溃。所有信息均为非敏感元数据。
/// </summary>
public static class AppInfo
{
    /// <summary>用户可见版本（如 0.6.0），来自集中版本来源 Directory.Build.props。</summary>
    public static string DisplayVersion { get; } = ReadDisplayVersion();

    /// <summary>MSIX 四段版本（如 0.6.0.0）；未打包回退到程序集版本。</summary>
    public static string PackageVersion { get; } = ReadPackageVersion();

    /// <summary>进程架构（x64 / x86 / arm64）。</summary>
    public static string Architecture { get; } = RuntimeInformation.ProcessArchitecture.ToString();

    /// <summary>Package Family Name；未打包时为空字符串。</summary>
    public static string PackageFamilyName { get; } = ReadPackageFamilyName();

    /// <summary>
    /// Package Identity Name；打包时读取实际安装身份，未打包时回退到
    /// 构建渠道对应的期望身份（ApiMonitor / JoKiy.ApiMonitor）。
    /// </summary>
    public static string PackageIdentity => ReadPackageIdentity();

    /// <summary>
    /// Package Publisher（CN=…）；打包时读取实际安装身份，未打包时回退到
    /// 构建渠道对应的期望 Publisher。
    /// </summary>
    public static string Publisher => ReadPublisher();

    /// <summary>Windows 版本（如 10.0.26100.0）。</summary>
    public static string WindowsVersion { get; } = ReadWindowsVersion();

    /// <summary>.NET 运行时版本（如 10.0.x）。</summary>
    public static string DotNetRuntimeVersion { get; } = Environment.Version.ToString();

    /// <summary>Windows App SDK 运行时版本（程序集名 Microsoft.WindowsAppRuntime）。</summary>
    public static string WindowsAppSdkVersion { get; } = ReadWindowsAppSdkVersion();

    /// <summary>是否打包运行（具有 Package Identity）。</summary>
    public static bool IsPackaged { get; } = IsPackagedRun();

    private static string ReadDisplayVersion()
    {
        try
        {
            string? informational = Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                // InformationalVersion 可能带 +build 后缀（SourceLink），只取主版本。
                int plus = informational.IndexOf('+');
                string value = plus > 0 ? informational[..plus] : informational;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // 回退到常量。
        }

        return "1.0.0";
    }

    private static string ReadPackageVersion()
    {
        try
        {
            if (Package.Current is { } package)
            {
                var version = package.Id.Version;
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
        }
        catch
        {
            // 未打包或 API 不可用时回退。
        }

        try
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.1";
        }
        catch
        {
            return "1.0.0.1";
        }
    }

    private static string ReadPackageFamilyName()
    {
        try
        {
            return Package.Current?.Id?.FamilyName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadWindowsVersion()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string ReadWindowsAppSdkVersion()
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(
                    a.GetName().Name,
                    "Microsoft.WindowsAppRuntime",
                    StringComparison.OrdinalIgnoreCase));
            return assembly?.GetName().Version?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static bool IsPackagedRun()
    {
        try
        {
            return Package.Current is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadPackageIdentity()
    {
        try
        {
            if (Package.Current is { } package)
            {
                return package.Id.Name;
            }
        }
        catch
        {
            // 未打包或 API 不可用时回退。
        }

        return DistributionChannelIdentity.ExpectedIdentityName(DistributionChannelConfig.Current);
    }

    private static string ReadPublisher()
    {
        try
        {
            if (Package.Current is { } package)
            {
                return package.Id.Publisher;
            }
        }
        catch
        {
            // 未打包或 API 不可用时回退。
        }

        return DistributionChannelIdentity.ExpectedPublisher(DistributionChannelConfig.Current);
    }
}
