using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class WebDavBackupSecurityTests
{
    [Fact]
    public void Settings_RejectHttpAndTraversal()
    {
        using var temp = new TempDirectory();
        using var service = new WebDavBackupService(new StubBackup(), new FakeSecretStore(), temp.Path);
        Assert.Throws<InvalidOperationException>(() => service.SaveSettings(new WebDavBackupSettings
        {
            Endpoint = "http://example.test/dav/",
            RemoteDirectory = "ApiMonitor/Backups",
        }));
        Assert.Throws<InvalidOperationException>(() => service.SaveSettings(new WebDavBackupSettings
        {
            Endpoint = "https://example.test/dav/",
            RemoteDirectory = "../Backups",
        }));
    }

    [Fact]
    public async Task Password_IsKeptOutOfSettingsJson()
    {
        using var temp = new TempDirectory();
        var secrets = new FakeSecretStore();
        using var service = new WebDavBackupService(new StubBackup(), secrets, temp.Path);
        await service.SetPasswordAsync("super-secret-value", CancellationToken.None);
        service.SaveSettings(new WebDavBackupSettings
        {
            Endpoint = "https://example.test/dav/",
            UserName = "user",
            RemoteDirectory = "ApiMonitor/Backups",
        });

        string json = File.ReadAllText(Path.Combine(temp.Path, WebDavBackupService.SettingsFileName));
        Assert.DoesNotContain("super-secret-value", json, StringComparison.Ordinal);
        Assert.Contains("super-secret-value", secrets.Secrets.Values);
    }

    private sealed class StubBackup : IPortableBackupService
    {
        public Task ExportAsync(string targetFilePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackupImportPreview> InspectAsync(string sourceFilePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackupImportResult> ImportAsync(string sourceFilePath, BackupMergePreference preference, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsValidBackupAsync(string sourceFilePath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
