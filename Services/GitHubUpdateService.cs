using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ApiMonitor.Services;

/// <summary>
/// GitHub 侧载渠道手动更新检查：只在用户点击“检查更新”时访问 GitHub REST
/// repos/KiYouJyo/ApiMonitor/releases/latest。
/// 要求：User-Agent=ApiMonitor/&lt;DisplayVersion&gt;；15 秒超时；不用用户 Token；
/// 不上传任何账户/余额/设备数据；不自动下载/安装；发现更新后打开 Release 页面。
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/KiYouJyo/ApiMonitor/releases/latest";

    private readonly IHttpRequestService _http;
    private readonly string _displayVersion;

    public GitHubUpdateService(IHttpRequestService http, string displayVersion)
    {
        _http = http;
        _displayVersion = displayVersion;
    }

    public DistributionChannel Channel => DistributionChannel.GitHubSideload;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd($"ApiMonitor/{_displayVersion}");

            using var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Failed,
                    ErrorMessage = L10n.Get("Update.NotFound404"),
                };
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Failed,
                    ErrorMessage = L10n.Get("Update.Forbidden403"),
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Failed,
                    ErrorMessage = L10n.Format("Update.HttpError", (int)response.StatusCode),
                };
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            string? tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            string? htmlUrl = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;

            if (string.IsNullOrWhiteSpace(tagName))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Failed,
                    ErrorMessage = L10n.Get("Update.IncompleteData"),
                };
            }

            // tag_name 形如 v1.0.0；比较语义版本。
            string normalized = tagName.Trim().TrimStart('v');
            int comparison = CompareVersions(normalized, _displayVersion);
            if (comparison > 0)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpdateAvailable,
                    LatestVersion = normalized,
                    ReleaseUrl = string.IsNullOrWhiteSpace(htmlUrl)
                        ? $"https://github.com/KiYouJyo/ApiMonitor/releases/tag/{tagName}"
                        : htmlUrl,
                };
            }

            if (comparison == 0)
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
            }

            return new UpdateCheckResult { Status = UpdateCheckStatus.DevVersionNewer };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Get("Update.Timeout"),
            };
        }
        catch (Exception ex) when (
            ex is HttpRequestException or IOException or JsonException or InvalidOperationException)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Format("Update.NetworkError", ex.GetType().Name),
            };
        }
    }

    /// <summary>语义版本比较（支持 x.y.z 与 x.y.z.w 四段）。</summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        int length = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < length; i++)
        {
            int x = i < pa.Length ? pa[i] : 0;
            int y = i < pb.Length ? pb[i] : 0;
            if (x != y)
            {
                return x.CompareTo(y);
            }
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
            {
                return Array.Empty<int>();
            }

            result[i] = value;
        }

        return result;
    }
}
