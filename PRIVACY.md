# ApiMonitor 隐私说明 / Privacy Policy

最后更新：2026-08-04 · 对应：v0.7.0（候选包，等待人工验收）

ApiMonitor 是一款本地运行的 Windows 桌面应用，用于查询并记录你自己的 API 账户余额，支持 **DeepSeek** 与 **OpenRouter** 两个 Provider。

## 版本说明

- **v0.7.0（候选包）**：主页 UI 整理（移除副标题与“打开紧凑窗口”按钮）、悬浮余额窗替代紧凑窗口（只展示一个选定账户的核心额度数字，本地展示）、应用图标整体替换；在 v0.6.0 之上原地升级，保留全部账户、凭据、历史与设置；旧紧凑窗口设置自动迁移为悬浮窗设置。
- **v0.6.0（正式 Release）**：数据洞察与消费估算（仅本机历史）、便携备份（不含密钥）、主题与三语（简体中文/English/日本語）、完整“关于”页与手动更新检查；在 v0.5.0 之上原地升级，保留全部账户、凭据、历史与设置。
- **v0.5.0（历史开发版本）**：通用余额指标（BalanceMetric）、多账户管理、OpenRouter Provider、Windows 通知中心低余额提醒。
- **v0.4.0（历史正式 Release）**：完整 ApiMonitor 包身份（`ApiMonitor` / `CN=ApiMonitorDev`），通知区域托盘驻留、关闭到托盘、单实例与可选的登录启动。
- v0.2.0 侧载包不会原地升级到 v0.3.x / v0.4.0 / v0.5.0 / v0.6.0 / v0.7.0；请先卸载旧包。

## 数据收集与处理

- 应用不要求你注册开发者账户，也不要求你自建服务器；**不新增任何遥测或开发者服务器**。
- 应用不向开发者服务器上传任何数据。
- 支持 DeepSeek 与 OpenRouter；**凭据只保存在 Windows Credential Locker（凭据管理器）** 的 ApiMonitor 资源中，只用于请求对应 Provider 的官方接口：
  - DeepSeek：`https://api.deepseek.com/user/balance`
  - OpenRouter 普通 API Key：`https://openrouter.ai/api/v1/key`
  - OpenRouter Management Key：`https://openrouter.ai/api/v1/credits`
- **OpenRouter Management Key 权限高于普通 API Key**，仅在需要查询账户总 Credits 时使用；密钥仍只保存在 Windows Credential Locker。OpenRouter 密钥只发送给 OpenRouter 官方接口，绝不发送到其他服务。
- API Key 不会写入任何配置文件（JSON）、日志、托盘 Tooltip、托盘菜单、命令行参数、StartupTask 或激活参数。
- 账户元数据、余额快照、历史记录、阈值、通知设置与通知状态只保存在本机应用数据目录，不会上传。
- 自动刷新只在应用运行期间执行；关闭应用后不会在后台运行。
- **关闭主窗口并隐藏到通知区域后，应用进程仍在运行**，自动刷新继续执行；只有选择“退出 ApiMonitor”或系统结束进程时才停止全部刷新并退出。
- 登录启动（登录 Windows 时启动）为**用户可选**功能，默认关闭；启用后仅在登录时驻留通知区域，不自动弹出主窗口。
- 悬浮余额窗只展示本机已有的选定账户余额快照（不包含完整账户列表与历史），不额外上传数据。
- 托盘状态（Tooltip 与菜单）只显示余额状态摘要（如“余额正常”“N 个指标低于阈值”“正在刷新”），不包含余额明细、账户名称、API Key 或错误堆栈。

## 数据洞察与消费估算（v0.6.0）

- 数据洞察页的趋势、每日消耗与预计可用天数**只基于本机历史记录计算**，不访问任何外部服务；界面标注“估算值”并说明“基于本机历史记录计算，实际消耗可能不同”。
- 估算结果不会触发低余额系统通知，也不被表述为 Provider 官方结论。

## 便携备份（v0.6.0）

- 便携备份（`.apimonitor-backup`，ZIP+JSON）只包含账户非敏感元数据、Provider 非敏感设置、余额历史、阈值与各类设置；**明确标记 containsSecrets=false**。
- **不导出**：API Key、Management Key、Credential Locker 内容、Authorization、日志、PFX、证书私钥、临时缓存、活动通知 Tag 与短期通知去重状态。
- 导入为安全合并：已有账户保留本机凭据；新账户标记“需要重新输入凭据”；历史按稳定 Id 去重；导入失败回滚。

## 手动更新检查（v0.6.0）

- 只在用户点击“检查更新”时访问 GitHub REST（`repos/KiYouJyo/ApiMonitor/releases/latest`），请求仅包含版本号（User-Agent）；**不上传账户、余额、历史或设备数据**。
- 发现新版本后由用户决定是否下载安装；应用不自动下载、不自动安装。

## 通知中心提醒（v0.5.0）

- 低余额与余额恢复通知**由本机 ApiMonitor 进程在运行期间生成**；不使用云端推送，不使用 WNS 远程推送，不经过任何第三方推送服务。
- 通知内容不包含 API Key；**通知参数只包含非敏感账户标识**（`action`、`accountId`、`providerId`、`metricId`），绝不包含 API Key、余额历史正文、Authorization、Credential Locker Resource 或本机文件路径。
- 通知仅按你在应用内开启的全局/账户设置发送；升级后**全局系统提醒默认关闭**，用户可随时关闭通知或清除通知。
- 选择“退出 ApiMonitor”后不再查询，也不再发送新提醒。
- 测试通知不查询 API、不改变阈值状态、不写入余额历史。

## 删除数据

- 你可以在应用中删除单个账户及其凭据、余额快照、历史记录、阈值、通知状态与通知中心中的活动通知。
- 卸载应用并删除其本地数据目录即可清除本机保存的配置与快照。
- 卸载应用通常也会移除该应用在 Credential Locker 中的凭据。

## 其他

- 当前版本无遥测、无广告、无崩溃上传、无开发者服务器。
- 当前版本无系统后台服务、无 UWP BackgroundTask、无云端推送。
- 应用日志仅记录错误类型与普通信息，不包含 API Key、Authorization 请求头或完整请求内容。

## 联系我们

如有隐私相关问题，请通过仓库 [Issues](https://github.com/KiYouJyo/ApiMonitor/issues) 或 GitHub Private Vulnerability Reporting 联系我们，切勿在公开渠道粘贴真实 API Key。
