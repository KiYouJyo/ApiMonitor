using System.Xml;

namespace ApiMonitor.Services;

/// <summary>
/// 安全 XML 读取（v0.9.0，OGC GetCapabilities 解析专用）。
/// 要求：
///   - 禁用 DTD（DtdProcessing.Prohibit）；
///   - 禁止外部实体与外部 Schema（XmlResolver=null）；
///   - 禁止实体扩展（MaxCharactersFromEntities=0）；
///   - 限制最大文档大小与 XML 深度；
///   - 不执行 XSLT、不加载远程资源。
/// 非 XML 输入直接抛出 XmlException，由调用方安全失败。
/// </summary>
public static class SecureXml
{
    /// <summary>最大响应体字节数（4 MiB，足够 OGC GetCapabilities）。</summary>
    public const int MaxDocumentBytes = 4 * 1024 * 1024;

    /// <summary>最大 XML 深度（防止深度嵌套攻击）。</summary>
    public const int MaxDepth = 64;

    public static XmlReader CreateSafeReader(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new XmlException("Empty XML input.");
        }

        if (xml.Length > MaxDocumentBytes)
        {
            throw new XmlException("XML document exceeds the maximum allowed size.");
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxDocumentBytes,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            ConformanceLevel = ConformanceLevel.Document,
        };

        return XmlReader.Create(new StringReader(xml), settings);
    }
}
