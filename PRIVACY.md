# 隐私说明

ApiBalanceMonitor（v0.1.0）的隐私约定：

- 应用不要求自建账户，不收集任何个人信息。
- 不把用户数据上传到开发者服务器；应用只在用户主动点击查询/测试时，
  直接请求用户配置的官方 API Provider 接口。
- API Key 仅用于访问用户配置的官方 API Provider（当前为 DeepSeek），
  通过 Windows 安全凭据存储（Credential Locker）保存在本机，
  不会写入普通 JSON、设置、日志、测试快照或导出数据。
- 普通配置（账户 ID、Provider ID、显示名称、凭据存在标记、时间戳）
  与最近一次余额快照保存在本机应用数据目录。
- 本地日志只记录时间、级别与不含敏感信息的消息；
  不记录 API Key、Authorization 请求头、完整 HTTP 请求或完整 API 响应正文。
- 第一阶段不包含遥测、崩溃上传、自动更新或任何数据导出功能。
