using System.Globalization;
using System.Text;
using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// v0.6.0：CSV 导出（数据洞察页“导出当前历史为 CSV”）。
/// 要求：
///   - UTF-8 with BOM；
///   - 正确转义逗号、引号和换行；
///   - 稳定英文机器可读列名；
///   - 时间 ISO 8601 UTC；
///   - 数值 invariant culture；
///   - 保留 Provider 单位与币种；
///   - 不含 API Key、Credential Locker 标识、Authorization、日志与本机路径。
/// CSV 只用于分析与表格软件，不作为 ApiMonitor 备份恢复格式。
/// </summary>
public interface ICsvHistoryExporter
{
    /// <summary>
    /// 把历史记录序列化为 CSV 文本（UTF-8 with BOM，调用方负责写入文件）。
    /// 每个 (账户, 指标) 组合输出一行；同一快照多指标拆成多行。
    /// </summary>
    Task<string> ExportAsync(
        IReadOnlyList<BalanceHistoryEntry> history,
        IReadOnlyDictionary<string, ApiAccount> accountsById,
        CancellationToken cancellationToken);
}

public sealed class CsvHistoryExporter : ICsvHistoryExporter
{
    // 稳定英文机器可读列名（不得本地化）。
    private static readonly string[] Columns =
    {
        "TimestampUtc",
        "AccountId",
        "AccountDisplayName",
        "ProviderId",
        "MetricId",
        "MetricDisplayName",
        "Unit",
        "AvailableAmount",
        "TotalAmount",
        "UsedAmount",
        "GrantedAmount",
        "ToppedUpAmount",
        "QuerySource",
        "ValueKind",
        "DetailedKind",
        "IntegerValue",
        "StatusValue",
        "BooleanValue",
        "TimestampValue",
    };

    public Task<string> ExportAsync(
        IReadOnlyList<BalanceHistoryEntry> history,
        IReadOnlyDictionary<string, ApiAccount> accountsById,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF'); // UTF-8 BOM（写文件时用 UTF8 编码写出）。
        sb.AppendLine(string.Join(",", Columns));

        var ordered = history
            .OrderBy(h => h.SucceededAtUtc)
            .ThenBy(h => h.Id);

        foreach (var entry in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            accountsById.TryGetValue(entry.AccountId, out var account);
            foreach (var metric in entry.Metrics)
            {
                sb.Append(Escape(entry.SucceededAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
                sb.Append(',');
                sb.Append(Escape(entry.AccountId));
                sb.Append(',');
                sb.Append(Escape(account?.DisplayName ?? string.Empty));
                sb.Append(',');
                sb.Append(Escape(entry.ProviderId));
                sb.Append(',');
                sb.Append(Escape(metric.MetricId));
                sb.Append(',');
                sb.Append(Escape(metric.DisplayName));
                sb.Append(',');
                sb.Append(Escape(metric.Unit));
                sb.Append(',');
                sb.Append(FormatDecimal(metric.AvailableAmount));
                sb.Append(',');
                sb.Append(FormatDecimal(metric.TotalAmount));
                sb.Append(',');
                sb.Append(FormatDecimal(metric.UsedAmount));
                sb.Append(',');
                sb.Append(FormatDecimal(metric.GrantedAmount));
                sb.Append(',');
                sb.Append(FormatDecimal(metric.ToppedUpAmount));
                sb.Append(',');
                sb.Append(Escape(entry.Source.ToString()));
                sb.Append(',');
                sb.Append(Escape(metric.ValueKind.ToString()));
                sb.Append(',');
                sb.Append(Escape(metric.DetailedKind?.ToString() ?? string.Empty));
                sb.Append(',');
                sb.Append(metric.IntegerValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                sb.Append(',');
                sb.Append(Escape(metric.StatusValue ?? string.Empty));
                sb.Append(',');
                sb.Append(metric.BooleanValue is { } b ? (b ? "true" : "false") : string.Empty);
                sb.Append(',');
                sb.Append(metric.TimestampValue?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
                sb.AppendLine();
            }
        }

        return Task.FromResult(sb.ToString());
    }

    /// <summary>未知值输出为空字符串（不写 0，不把未知当作 0）。</summary>
    private static string FormatDecimal(decimal? value) =>
        value is { } v ? v.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>RFC 4180 转义：双引号翻倍，含逗号/引号/换行的字段加引号。</summary>
    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        string escaped = value.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{escaped}\"" : escaped;
    }
}
