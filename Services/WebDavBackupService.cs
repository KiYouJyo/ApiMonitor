using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace ApiMonitor.Services;

public sealed class WebDavBackupSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string RemoteDirectory { get; set; } = "ApiMonitor/Backups";
    public bool AutoBackupEnabled { get; set; }
    public DateTimeOffset? LastBackupUtc { get; set; }
}

public sealed record WebDavBackupEntry(string Name, Uri Uri, long? Size, DateTimeOffset? LastModifiedUtc);

public interface IWebDavBackupService
{
    WebDavBackupSettings LoadSettings();
    void SaveSettings(WebDavBackupSettings settings);
    Task SetPasswordAsync(string password, CancellationToken cancellationToken);
    Task TestConnectionAsync(WebDavBackupSettings settings, CancellationToken cancellationToken);
    Task<WebDavBackupEntry> UploadAsync(WebDavBackupSettings settings, CancellationToken cancellationToken);
    Task<IReadOnlyList<WebDavBackupEntry>> ListAsync(WebDavBackupSettings settings, CancellationToken cancellationToken);
    Task<BackupImportResult?> RestoreLatestAsync(WebDavBackupSettings settings, CancellationToken cancellationToken);
}

/// <summary>
/// WebDAV 仅作为既有 .apimonitor-backup 的传输层。密码只进入 Credential Locker；
/// 配置 JSON 不含密码。强制 HTTPS 且禁用自动重定向，避免 Authorization 跨站转发。
/// </summary>
public sealed class WebDavBackupService : IWebDavBackupService, IDisposable
{
    internal const string CredentialId = "__apimonitor_webdav_backup__";
    internal const string SettingsFileName = "webdav-settings.json";

    private readonly IPortableBackupService _backup;
    private readonly ISecretStore _secrets;
    private readonly string _settingsPath;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public WebDavBackupService(
        IPortableBackupService backup,
        ISecretStore secrets,
        string dataDirectory,
        HttpClient? httpClient = null)
    {
        _backup = backup;
        _secrets = secrets;
        _settingsPath = Path.Combine(dataDirectory, SettingsFileName);
        Directory.CreateDirectory(dataDirectory);

        if (httpClient is not null)
        {
            _http = httpClient;
            _ownsHttp = false;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            _ownsHttp = true;
        }
    }

    public WebDavBackupSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new WebDavBackupSettings();
            return JsonSerializer.Deserialize<WebDavBackupSettings>(File.ReadAllText(_settingsPath), JsonOptions())
                ?? new WebDavBackupSettings();
        }
        catch
        {
            return new WebDavBackupSettings();
        }
    }

    public void SaveSettings(WebDavBackupSettings settings)
    {
        ValidateSettings(settings, requireCredentials: false);
        string temp = _settingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions()));
        File.Move(temp, _settingsPath, true);
    }

    public Task SetPasswordAsync(string password, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(password)
            ? Task.CompletedTask
            : _secrets.SetAsync(CredentialId, password, cancellationToken);

    public async Task TestConnectionAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
    {
        var target = BuildRemoteDirectoryUri(settings);
        using var request = await CreateRequestAsync(new HttpMethod("PROPFIND"), target, settings, cancellationToken);
        request.Headers.TryAddWithoutValidation("Depth", "0");
        request.Content = DavPropertyRequest();
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureDavSuccess(response, allowMultiStatus: true);
    }

    public async Task<WebDavBackupEntry> UploadAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
    {
        ValidateSettings(settings, requireCredentials: true);
        await EnsureRemoteDirectoryAsync(settings, cancellationToken);

        string name = $"ApiMonitor-backup-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{PortableBackupConstants.Extension}";
        string tempPath = Path.Combine(Path.GetTempPath(), name);
        try
        {
            await _backup.ExportAsync(tempPath, cancellationToken);
            Uri target = new(BuildRemoteDirectoryUri(settings), Uri.EscapeDataString(name));
            using var request = await CreateRequestAsync(HttpMethod.Put, target, settings, cancellationToken);
            request.Content = new StreamContent(new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            EnsureDavSuccess(response, allowMultiStatus: false);

            var info = new FileInfo(tempPath);
            settings.LastBackupUtc = DateTimeOffset.UtcNow;
            SaveSettings(settings);
            return new WebDavBackupEntry(name, target, info.Length, settings.LastBackupUtc);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public async Task<IReadOnlyList<WebDavBackupEntry>> ListAsync(
        WebDavBackupSettings settings,
        CancellationToken cancellationToken)
    {
        Uri directory = BuildRemoteDirectoryUri(settings);
        using var request = await CreateRequestAsync(new HttpMethod("PROPFIND"), directory, settings, cancellationToken);
        request.Headers.TryAddWithoutValidation("Depth", "1");
        request.Content = DavPropertyRequest();
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureDavSuccess(response, allowMultiStatus: true);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var xmlSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(stream, xmlSettings);
        var document = XDocument.Load(reader, LoadOptions.None);
        XNamespace dav = "DAV:";
        var result = new List<WebDavBackupEntry>();

        foreach (var item in document.Descendants(dav + "response"))
        {
            string? href = item.Element(dav + "href")?.Value;
            if (string.IsNullOrWhiteSpace(href)) continue;
            Uri uri;
            try { uri = new Uri(directory, href); }
            catch { continue; }
            string name = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath.TrimEnd('/')));
            if (!name.EndsWith('.' + PortableBackupConstants.Extension, StringComparison.OrdinalIgnoreCase)) continue;

            string? sizeText = item.Descendants(dav + "getcontentlength").FirstOrDefault()?.Value;
            long? size = long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out long parsedSize)
                ? parsedSize : null;
            string? modifiedText = item.Descendants(dav + "getlastmodified").FirstOrDefault()?.Value;
            DateTimeOffset? modified = DateTimeOffset.TryParse(
                modifiedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate)
                ? parsedDate.ToUniversalTime() : null;
            result.Add(new WebDavBackupEntry(name, uri, size, modified));
        }

        return result
            .OrderByDescending(x => x.LastModifiedUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<BackupImportResult?> RestoreLatestAsync(
        WebDavBackupSettings settings,
        CancellationToken cancellationToken)
    {
        var entries = await ListAsync(settings, cancellationToken);
        var latest = entries.FirstOrDefault();
        if (latest is null) return null;

        string tempPath = Path.Combine(Path.GetTempPath(), "ApiMonitor-restore-" + Guid.NewGuid().ToString("N") + "." + PortableBackupConstants.Extension);
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, latest.Uri, settings, cancellationToken);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            EnsureDavSuccess(response, allowMultiStatus: false);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            if (!await _backup.IsValidBackupAsync(tempPath, cancellationToken))
            {
                throw new InvalidDataException("InvalidWebDavBackup");
            }

            return await _backup.ImportAsync(tempPath, BackupMergePreference.KeepLocal, cancellationToken);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task EnsureRemoteDirectoryAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
    {
        Uri endpoint = ValidateEndpoint(settings.Endpoint);
        string current = EnsureTrailingSlash(endpoint.AbsoluteUri);
        foreach (string segment in SplitRemoteDirectory(settings.RemoteDirectory))
        {
            current += Uri.EscapeDataString(segment) + "/";
            using var request = await CreateRequestAsync(new HttpMethod("MKCOL"), new Uri(current), settings, cancellationToken);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            int code = (int)response.StatusCode;
            if (code is 200 or 201 or 204 or 405) continue;
            EnsureDavSuccess(response, allowMultiStatus: false);
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        Uri uri,
        WebDavBackupSettings settings,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settings, requireCredentials: true);
        string? password = await _secrets.GetAsync(CredentialId, cancellationToken);
        if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("WebDavPasswordRequired");
        string raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.UserName + ":" + password));
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
        request.Headers.UserAgent.ParseAdd($"ApiMonitor/{AppInfo.DisplayVersion}");
        return request;
    }

    private static StringContent DavPropertyRequest() => new(
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:getlastmodified/><d:getcontentlength/><d:resourcetype/></d:prop></d:propfind>",
        Encoding.UTF8,
        "application/xml");

    private static Uri BuildRemoteDirectoryUri(WebDavBackupSettings settings)
    {
        Uri endpoint = ValidateEndpoint(settings.Endpoint);
        string path = string.Join('/', SplitRemoteDirectory(settings.RemoteDirectory).Select(Uri.EscapeDataString));
        return new Uri(EnsureTrailingSlash(endpoint.AbsoluteUri) + (path.Length == 0 ? string.Empty : path + "/"));
    }

    private static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("WebDavInvalidEndpoint");
        }
        return uri;
    }

    private static string[] SplitRemoteDirectory(string value)
    {
        var segments = (value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(s => s is "." or "..")) throw new InvalidOperationException("WebDavInvalidEndpoint");
        return segments;
    }

    private static void ValidateSettings(WebDavBackupSettings settings, bool requireCredentials)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _ = ValidateEndpoint(settings.Endpoint);
        _ = SplitRemoteDirectory(settings.RemoteDirectory);
        if (requireCredentials && string.IsNullOrWhiteSpace(settings.UserName))
        {
            throw new InvalidOperationException("WebDavPasswordRequired");
        }
    }

    private static void EnsureDavSuccess(HttpResponseMessage response, bool allowMultiStatus)
    {
        int code = (int)response.StatusCode;
        if (response.IsSuccessStatusCode || (allowMultiStatus && code == 207)) return;
        if (code is >= 300 and < 400)
        {
            throw new HttpRequestException("WebDAV redirect blocked.", null, response.StatusCode);
        }
        throw new HttpRequestException($"WebDAV HTTP {code}.", null, response.StatusCode);
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";
    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
