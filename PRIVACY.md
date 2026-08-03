# ApiMonitor 隐私说明 / Privacy Policy

最后更新：2026-08-03 · 版本：v0.2.0

ApiMonitor 是一款本地运行的 Windows 桌面应用，用于查询并记录你自己的 API 账户余额。

## 数据收集与处理

- 应用不要求你注册开发者账户，也不要求你自建服务器。
- 应用不向开发者服务器上传任何数据。
- 你的 API Key 保存在 Windows Credential Locker（凭据管理器）中，只用于请求对应 Provider 的官方接口；当前仅访问 DeepSeek 官方余额接口。
- 账户元数据、余额快照、历史记录和设置只保存在本机应用数据目录，不会上传。
- 自动刷新只在应用运行期间执行；关闭应用后不会在后台运行。
- 应用会在复制 API Key 时短暂写入 Windows 剪贴板，并在约 30 秒后尝试安全清理；如果期间你复制了其他内容，应用不会清空你的新内容。

## 删除数据

- 你可以在应用中删除单个账户及其凭据、余额快照与历史记录。
- 卸载应用并删除其本地数据目录即可清除本机保存的配置与快照。
- 卸载应用通常也会移除该应用在 Credential Locker 中的凭据。

## 其他

- 当前版本无遥测、无广告、无崩溃上传。
- 当前版本无系统后台任务、无托盘驻留、无系统通知。
- 应用日志仅记录错误类型与普通信息，不包含 API Key、Authorization 请求头或完整请求内容。

## 联系我们

如有隐私相关问题，请通过仓库 [Issues](https://github.com/KiYouJyo/ApiMonitor/issues) 或 GitHub Private Vulnerability Reporting 联系我们，切勿在公开渠道粘贴真实 API Key。
