using System.Globalization;
using System.Net;
using System.Text.Json;
using Windows.Management.Deployment;

namespace ApiMonitor.Services;

/// <summary>
/// GitHub 侧载渠道更新：检查 Release，优先选择 .msixbundle/.msix 资产；用户主动
/// 请求后下载到临时目录，再交给 Windows PackageManager 校验并部署。
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/KiYouJyo/ApiMonitor/releases/latest";
    private readonly IHttpRequestService _http;
    private readonly string _displayVersion;
    private Uri? _pendingPackageUri;
    private string? _pendingPackageName;

    public GitHubUpdateService(IHttpRequestService http, string displayVersion)
    {
        _http = http;
        _displayVersion = displayVersion;
    }

    public DistributionChannel Channel => DistributionChannel.GitHubSideload;
    public bool HasInstallablePackage => _pendingPackageUri is not null;
    public event EventHandler<double>? DownloadProgressChanged;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        _pendingPackageUri = null;
        _pendingPackageName = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd($"ApiMonitor/{_displayVersion}");
            using var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Failed(L10n.Get("Update.NotFound404"));
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return Failed(L10n.Get("Update.Forbidden403"));
            if (!response.IsSuccessStatusCode)
                return Failed(L10n.Format("Update.HttpError", (int)response.StatusCode));

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            string? tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            string? htmlUrl = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;
            if (string.IsNullOrWhiteSpace(tagName)) return Failed(L10n.Get("Update.IncompleteData"));

            string normalized = tagName.Trim().TrimStart('v');
            int comparison = CompareVersions(normalized, _displayVersion);
            if (comparison > 0)
            {
                SelectInstallAsset(root);
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpdateAvailable,
                    LatestVersion = normalized,
                    ReleaseUrl = string.IsNullOrWhiteSpace(htmlUrl)
                        ? $"https://github.com/KiYouJyo/ApiMonitor/releases/tag/{tagName}"
                        : htmlUrl,
                    CanInstallInApp = _pendingPackageUri is not null,
                    InstallAssetName = _pendingPackageName,
                };
            }
            if (comparison == 0) return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
            return new UpdateCheckResult { Status = UpdateCheckStatus.DevVersionNewer };
        }
        catch (OperationCanceledException)
        {
            return Failed(L10n.Get("Update.Timeout"));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException)
        {
            return Failed(L10n.Format("Update.NetworkError", ex.GetType().Name));
        }
    }

    /// <summary>用户主动下载并安装当前检查到的签名包。</summary>
    public async Task<UpdateCheckResult> RequestInstallAsync(CancellationToken cancellationToken)
    {
        if (_pendingPackageUri is null || _pendingPackageName is null)
            return Failed(L10n.Get("Update.NoPackageAsset"));
        if (!string.Equals(_pendingPackageUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return Failed(L10n.Get("Update.InvalidPackageUrl"));

        string directory = Path.Combine(Path.GetTempPath(), "ApiMonitor", "Updates");
        Directory.CreateDirectory(directory);
        CleanupOldPackages(directory);
        string localPath = Path.Combine(directory, Path.GetFileName(_pendingPackageName));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _pendingPackageUri);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            request.Headers.UserAgent.ParseAdd($"ApiMonitor/{_displayVersion}");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return Failed(L10n.Get("Update.DownloadFailed"));

            long? total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 131072, useAsync: true);
            var buffer = new byte[131072];
            long written = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                if (total is > 0)
                    DownloadProgressChanged?.Invoke(this, Math.Clamp((double)written / total.Value, 0, 1));
            }
            await output.FlushAsync(cancellationToken);
            DownloadProgressChanged?.Invoke(this, 1);

            var packageManager = new PackageManager();
            var deployment = packageManager.AddPackageAsync(
                new Uri(Path.GetFullPath(localPath)),
                Array.Empty<Uri>(),
                DeploymentOptions.ForceApplicationShutdown);
            await deployment.AsTask(cancellationToken);
            return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
        }
        catch (OperationCanceledException)
        {
            return Failed(L10n.Get("Update.InstallFailed"));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return Failed(L10n.Format("Update.InstallFailedDetail", ex.GetType().Name));
        }
    }

    private void SelectInstallAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return;
        var candidates = new List<(int Score, string Name, Uri Uri)>();
        foreach (var asset in assets.EnumerateArray())
        {
            string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? download = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(download)) continue;
            if (!Uri.TryCreate(download, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
            string lower = name.ToLowerInvariant();
            int score = lower.EndsWith(".msixbundle", StringComparison.Ordinal) ? 100
                : lower.EndsWith(".msix", StringComparison.Ordinal) ? 60 : 0;
            if (score == 0) continue;
            string arch = AppInfo.Architecture.ToLowerInvariant();
            if (lower.Contains(arch, StringComparison.Ordinal)) score += 20;
            if (lower.Contains("unsigned", StringComparison.Ordinal) || lower.Contains("selfsigned", StringComparison.Ordinal)) score -= 80;
            candidates.Add((score, name, uri));
        }

        var best = candidates.OrderByDescending(x => x.Score).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (best.Score > 0)
        {
            _pendingPackageUri = best.Uri;
            _pendingPackageName = best.Name;
        }
    }

    public static int CompareVersions(string a, string b)
    {
        var pa = Parse(a); var pb = Parse(b);
        int length = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < length; i++)
        {
            int x = i < pa.Length ? pa[i] : 0;
            int y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static int[] Parse(string version)
    {
        var parts = version.Split('.');
        var result = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                return Array.Empty<int>();
            result[i] = value;
        }
        return result;
    }

    private static UpdateCheckResult Failed(string message) => new() { Status = UpdateCheckStatus.Failed, ErrorMessage = message };

    private static void CleanupOldPackages(string directory)
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                var info = new FileInfo(path);
                if (DateTimeOffset.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromDays(7)) info.Delete();
            }
        }
        catch { }
    }
}
