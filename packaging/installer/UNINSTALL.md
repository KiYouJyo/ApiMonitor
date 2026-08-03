# ApiMonitor v0.6.0 卸载说明（Uninstall.cmd，候选包版本 0.6.0.0）

卸载与安装一样简单：双击 **`Uninstall.cmd`** 并按提示操作。

> v0.6.0 中关闭主窗口默认仅隐藏到通知区域；卸载前建议先右键托盘图标选择**“退出 ApiMonitor”**，让脚本能干净结束进程。

## 卸载程序做了什么

1. 检查当前用户是否安装了 `ApiMonitor` 包（**精确 Identity 匹配，不使用 `-AllUsers`**）。
2. 如果应用仍在运行，先尝试通过托盘/窗口正常退出，再结束残留进程（不会强杀其他同名进程）。
3. 卸载当前用户的 ApiMonitor MSIX。
4. 可选：按**完整 Thumbprint**（`CN=ApiMonitorDev`）清理 Local Machine > Trusted People 中的开发证书；仅当没有其他包仍使用该 Publisher 时执行。

卸载日志写入 `%TEMP%\ApiMonitor-Uninstall-*.log`，不包含 API Key、Credential Locker 内容或余额数据。

## 数据说明

- 账户 JSON、余额历史与设置位于应用 LocalState 中；原地升级（v0.5.0 → v0.6.0.0）会保留这些数据，卸载则不会保留。
- API Key 保存在 Windows Credential Locker（资源 `ApiMonitor`）中；卸载通常会一并移除，但如遇到残留，可在“凭据管理器 > Windows 凭据”中手动清理 `ApiMonitor` 条目。
- 卸载后，通知激活不会留下无效注册（应用退出时已注销 AppNotification）。

## 常见问题

- **提示“未安装”**：说明当前用户没有安装 ApiMonitor，无需处理。
- **提示“正在运行”**：先从托盘菜单选择“退出 ApiMonitor”，或按提示关闭窗口后重试。
- **证书清理被阻止**：可能仍有其他包使用同一 Publisher，脚本会跳过清理并明确提示。
