using System.Text.Json;

namespace ApiMonitor.Services;

/// <summary>
/// JSON 文件的原子写入与容错读取：写入采用“临时文件 + 替换”，
/// 读取失败时把损坏文件备份为 .corrupt-&lt;时间戳&gt;.json 并返回空数据，
/// 保证文件损坏不会导致应用无法启动。
/// </summary>
public static class AtomicJsonFile
{
    public sealed class LoadResult<T>
    {
        public required T Data { get; init; }

        public string? RecoveryMessage { get; init; }
    }

    public static async Task WriteAsync<T>(
        string directory,
        string fileName,
        T data,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, fileName);
        // 唯一临时文件：避免多实例/并发写盘时同一 .tmp 冲突（v0.6.0 修复）。
        string tempPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, data, options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            MoveWithRetry(tempPath, path);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // 清理失败不影响结果。
            }
        }
    }

    /// <summary>覆盖移动，失败时短暂重试（目标文件可能被短暂打开）。</summary>
    private static void MoveWithRetry(string sourcePath, string destinationPath)
    {
        const int maxAttempts = 3;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts - 1)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts - 1)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    public static async Task<LoadResult<T>> ReadOrRecoverAsync<T>(
        string directory,
        string fileName,
        JsonSerializerOptions options,
        Func<T> emptyFactory,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return new LoadResult<T> { Data = emptyFactory() };
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            T? data = await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken);
            if (data is null)
            {
                string backup = await BackupCorruptFileAsync(directory, fileName, cancellationToken);
                return new LoadResult<T>
                {
                    Data = emptyFactory(),
                    RecoveryMessage = BuildRecoveryMessage(fileName, backup),
                };
            }

            return new LoadResult<T> { Data = data };
        }
        catch (Exception ex) when (
            ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            string backup = await BackupCorruptFileAsync(directory, fileName, cancellationToken);
            return new LoadResult<T>
            {
                Data = emptyFactory(),
                RecoveryMessage = BuildRecoveryMessage(fileName, backup),
            };
        }
    }

    public static async Task<string> BackupCorruptFileAsync(
        string directory,
        string fileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        string backupPath = Path.Combine(directory, $"{fileName}.corrupt-{stamp}.json");

        if (File.Exists(backupPath))
        {
            backupPath = Path.Combine(directory, $"{fileName}.corrupt-{stamp}-{Guid.NewGuid():N}.json");
        }

        File.Move(path, backupPath);
        return backupPath;
    }

    private static string BuildRecoveryMessage(string fileName, string backupPath) =>
        string.IsNullOrEmpty(backupPath)
            ? $"{fileName} 无法读取，已重置。"
            : $"{fileName} 内容损坏，已备份为 {Path.GetFileName(backupPath)} 并重置。";
}
