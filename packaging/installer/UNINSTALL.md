# ApiMonitor v1.0.0 卸载说明（Uninstall.cmd，GitHub 侧载包版本 1.0.0.2）

卸载 GitHub 侧载版使用随包提供的 `Uninstall.cmd`（双击即可，会请求一次 UAC）：

1. **建议先退出应用**：右键通知区域托盘图标，选择**“退出 ApiMonitor”**，让脚本能干净结束进程。
2. 双击 **`Uninstall.cmd`**，按提示操作。
3. 脚本会询问是否同时移除开发证书（`CN=ApiMonitorDev`）；选择移除即可清理本机证书存储。

> v1.0.0 中关闭主窗口默认仅隐藏到通知区域；卸载前请先通过托盘菜单退出应用。

## 卸载会删除什么

- MSIX 程序包及其安装文件。
- 该包身份（`ApiMonitor_cx0n152q1hsh2`）下的 LocalState：账户 JSON、余额历史、阈值与设置。
- 通常也会移除该应用在 Windows Credential Locker 中的凭据条目。

## 卸载不会影响什么

- **Microsoft Store 版**（`JoKiy.ApiMonitor_4wdwgytaw3v2m`）是独立 Package Family：卸载 GitHub 版不会影响 Store 版，两者不能相互覆盖升级。
- 你在应用内导出的便携备份（`.apimonitor-backup`）保留在导出位置，可用于重新安装后的导入（导入不包含密钥，需要重新输入 API Key）。

## 关于数据

- 原地升级（v0.9.0 → v1.0.0）会保留本地数据；**卸载不会保留**。
- 如果卸载前需要保留账户与历史，请先使用应用内“便携备份”导出，再卸载。
