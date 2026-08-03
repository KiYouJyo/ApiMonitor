using System.Text;

namespace ApiMonitor.Services;

/// <summary>
/// 轻量本地日志。调用方必须保证消息不含 API Key、
/// Authorization 请求头、完整 HTTP 请求/响应正文或凭据存储内容。
/// </summary>
public sealed class AppLog
{
    private readonly string _filePath;
    private readonly object _gate = new();

    public AppLog(string directory)
    {
        _filePath = Path.Combine(directory, "app.log");
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        try
        {
            string line = string.Concat(
                DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                " [",
                level,
                "] ",
                message,
                Environment.NewLine);

            lock (_gate)
            {
                File.AppendAllText(_filePath, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // 日志失败绝不能导致应用崩溃。
        }
    }
}
