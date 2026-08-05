# ApiMonitor 隐私说明 / Privacy Policy

最后更新：2026-08-05 · 对应：v0.9.0（正式 Release）

ApiMonitor 是一款本地运行的 Windows 桌面应用，用于监测你自己的 API 账户余额、Credits、凭据状态与地图/GIS 服务健康，支持 **11 个 Provider**：AI 余额 Provider（**DeepSeek**、**OpenRouter**、**Moonshot / Kimi**、**SiliconFlow**、**xAI**）与地图/GIS 健康 Provider（**高德开放平台**、**百度地图开放平台**、**腾讯位置服务**、**天地图**、**SuperMap iServer**、通用 **OGC** 服务）。

## 版本说明

- **v0.9.0（正式 Release）**：新增 6 个地图/GIS 健康 Provider（高德、百度地图、腾讯位置服务、天地图、SuperMap iServer、通用 OGC）；余额、Credits 与服务健康指标分开处理，地图 Provider 不进入资金余额汇总；多槽位凭据（Key+SK、Basic、Bearer Token、Query Token）仍只保存在 Windows Credential Locker；健康探测使用固定公开输入，每次可能消耗一次调用额度，新地图账户默认关闭自动刷新；OGC 默认只调用 GetCapabilities；自托管 GIS 只访问用户配置的地址；在 v0.8.0 之上原地升级，保留全部账户、凭据、历史与设置，数据 schema 保持 v3。
- **v0.8.0（未独立发布，功能并入 v0.9.0）**：新增 Moonshot / Kimi、SiliconFlow、xAI 三个余额 Provider；余额查询只调用官方 GET 接口，不发送模型推理请求；新增凭据请求 HTTPS 主机白名单与有限重试；账户编辑器支持动态非敏感配置字段（xAI Team ID），编辑中切换 Provider 不会沿用旧凭据；在 v0.7.0 之上原地升级，保留全部账户、凭据、历史与设置，数据 schema 保持 v3。
- **v0.7.0（正式 Release）**：主页顶部布局精简（移除副标题与“打开紧凑窗口”按钮）、悬浮余额窗替代紧凑窗口（固定尺寸、黑白单层小方块，只展示一个选定账户的核心额度数字，支持 Windows 原生流畅拖动并记住账户与位置，全部本地展示）、应用/标题栏/任务栏/开始菜单/托盘图标整体替换；在 v0.6.0 之上原地升级，保留全部账户、凭据、历史与设置；旧紧凑窗口设置自动迁移为悬浮窗设置。
- **v0.6.0（正式 Release）**：数据洞察与消费估算（仅本机历史）、便携备份（不含密钥）、主题与三语（简体中文/English/日本語）、完整“关于”页与手动更新检查；在 v0.5.0 之上原地升级，保留全部账户、凭据、历史与设置。
- **v0.5.0（历史开发版本）**：通用余额指标（BalanceMetric）、多账户管理、OpenRouter Provider、Windows 通知中心低余额提醒。
- **v0.4.0（历史正式 Release）**：完整 ApiMonitor 包身份（`ApiMonitor` / `CN=ApiMonitorDev`），通知区域托盘驻留、关闭到托盘、单实例与可选的登录启动。
- v0.2.0 侧载包不会原地升级到 v0.3.x / v0.4.0 / v0.5.0 / v0.6.0 / v0.7.0；请先卸载旧包。

## 数据收集与处理

- 应用不要求你注册开发者账户，也不要求你自建服务器；**不新增任何遥测或开发者服务器**。
- 应用不向开发者服务器上传任何数据。
- 支持 11 个 Provider；**凭据只保存在 Windows Credential Locker（凭据管理器）** 的 ApiMonitor 资源中，只用于请求对应 Provider 的官方接口或用户明确配置的自托管地址：
  - DeepSeek：`https://api.deepseek.com/user/balance`
  - OpenRouter 普通 API Key：`https://openrouter.ai/api/v1/key`
  - OpenRouter Management Key：`https://openrouter.ai/api/v1/credits`
  - Moonshot / Kimi：`https://api.moonshot.cn/v1/users/me/balance`（普通 API Key）
  - SiliconFlow：`https://api.siliconflow.cn/v1/user/info`（普通 API Key）
  - xAI：`https://management-api.x.ai/v1/billing/teams/{team_id}/prepaid/balance`（Management Key；需要 Team ID）
  - 高德：`https://restapi.amap.com/v3/geocode/geo`（固定公开地址健康探测；Key，可选 SK 数字签名）
  - 百度地图：`https://api.map.baidu.com/geocoding/v3/`（固定公开地址健康探测；服务端 AK，可选 SK）
  - 腾讯位置服务：`https://apis.map.qq.com/ws/district/v1/list`（固定公开地址健康探测；Key，可选 SK）
  - 天地图：`https://api.tianditu.gov.cn/v2/search`（固定公开地址健康探测；Token）
  - SuperMap iServer：用户配置的自托管地址 `{baseUrl}/iserver/services.json`（可选管理状态探测默认关闭）
  - 通用 OGC：用户配置的 GetCapabilities URL（WMS 1.1.1/1.3.0、WMTS 1.0.0、WFS 1.0.0/2.0.0；默认只调用 GetCapabilities）
- **OpenRouter Management Key 权限高于普通 API Key**，仅在需要查询账户总 Credits 时使用；密钥仍只保存在 Windows Credential Locker。OpenRouter 密钥只发送给 OpenRouter 官方接口，绝不发送到其他服务。
- **xAI 余额查询必须使用 Management Key 并填写 Team ID**：普通 xAI 模型 API Key 无法查询余额；xAI Management Key 只发往 `management-api.x.ai` 官方主机，绝不发往模型推理端点。Team ID 属于非敏感账户配置，保存在本机账户数据与备份中。
- 所有凭据请求在发送前都会校验目标：AI 余额 Provider 与地图 Provider 必须是官方 HTTPS 主机（白名单），地图 Provider 不允许自定义 Base URL；自托管 GIS 仅允许 http/https（HTTP 需显式确认）并只访问用户配置的地址。余额查询只调用 GET 且无副作用的官方接口，不会产生 Token 消费；地图健康探测使用固定公开输入，每次探测可能消耗一次调用额度（界面明确提示）。
- 地图/GIS 探测不抓取任何厂商控制台、不扫描局域网、不探测其他端口；不跟随重定向，凭据不会转发到其他 Origin；自托管凭据不跨主机、跨端口或 HTTPS→HTTP 降级转发。
- OGC 响应使用安全 XML 解析（禁用 DTD/外部实体/实体扩展，限制大小与深度）；非 XML 响应安全拒绝。
- 四家地图平台没有公开的精确剩余配额接口，应用不伪造剩余次数；相关配额值保持未知（null）。
- 新地图账户默认关闭自动刷新；启用后默认 6 小时、最短 1 小时；自托管 GIS 最短 5 分钟；429/配额/401/403/Key 无效不自动重试。
- API Key、SK、Token 等凭据不会写入任何配置文件（JSON）、日志、托盘 Tooltip、托盘菜单、命令行参数、StartupTask 或激活参数。
- 日志剥离 `key/ak/tk/sig/sn/token` 等敏感查询参数；异常不含完整请求 URI。
- SiliconFlow 余额接口只读取余额字段；用户昵称、头像、邮箱等资料不会被读取保存，完整响应不会写入日志或本地文件。
- 账户元数据、余额快照、历史记录、阈值、通知设置与通知状态只保存在本机应用数据目录，不会上传。
- 自动刷新只在应用运行期间执行；关闭应用后不会在后台运行。
- **关闭主窗口并隐藏到通知区域后，应用进程仍在运行**，自动刷新继续执行；只有选择“退出 ApiMonitor”或系统结束进程时才停止全部刷新并退出。
- 登录启动（登录 Windows 时启动）为**用户可选**功能，默认关闭；启用后仅在登录时驻留通知区域，不自动弹出主窗口。
- 托盘状态（Tooltip 与菜单）只显示余额状态摘要（如“余额正常”“N 个指标低于阈值”“正在刷新”），不包含余额明细、账户名称、API Key 或错误堆栈。

## 悬浮余额窗（v0.7.0）

- 悬浮窗只显示本机已有的选定账户余额数据（账户名、Provider、主额度数字、单位与简短状态），不包含 API Key、完整账户列表或历史记录。
- **不新增任何网络请求或遥测**：悬浮窗内容全部来自应用本地已保存的余额快照；打开、切换或关闭悬浮窗不会额外访问任何 Provider 或开发者服务器。
- 悬浮窗**不显示 API Key**；API Key 始终只保存在 Windows Credential Locker。
- 打开与切换：在主页任一账户卡片点击“设为悬浮窗”，或右键托盘图标选择“打开悬浮窗”；再次操作可切换显示的账户。
- 关闭：右键托盘图标选择“关闭悬浮窗”；关闭悬浮窗不会退出 ApiMonitor，之后可随时通过账户卡片或托盘再次打开。
- 若悬浮窗被拖到屏幕外、显示器被拔出或工作区变化导致位置失效，应用会安全恢复到可见位置（必要时回到主显示器居中）。
- 请勿在 Issue、备份文件、日志或截图（含悬浮窗）中提交真实 API Key。

## 地图/GIS 健康监测（v0.9.0）

- 高德、百度地图、腾讯位置服务、天地图使用官方 Web 服务接口的固定公开查询做健康探测（地理编码、行政区划列表、地名搜索等），不读取、不保存任何业务查询数据；每次探测可能消耗一次 API 调用额度。
- 服务状态、凭据状态、权限状态、配额状态与延迟只用于本机展示与历史；地图 Provider 绝不进入资金余额汇总，也不显示伪造的剩余配额。
- SuperMap iServer 与通用 OGC 为自托管服务：应用只访问你配置的服务地址；OGC 默认只调用 GetCapabilities（WMS/WMTS/WFS），绝不调用 GetMap/GetFeature；SuperMap 管理状态探测默认关闭。
- 服务健康通知（服务不可用/恢复、凭据无效、权限不足、配额耗尽、预期服务/图层缺失与恢复等）由本机进程生成，内容不含 Key、Token、完整 URL、内网路径或服务目录内容。

## 数据洞察与消费估算（v0.6.0）

- 数据洞察页的趋势、每日消耗与预计可用天数**只基于本机历史记录计算**，不访问任何外部服务；界面标注“估算值”并说明“基于本机历史记录计算，实际消耗可能不同”。
- 估算结果不会触发低余额系统通知，也不被表述为 Provider 官方结论。
- v0.9.0 起数据洞察还包含地图/GIS 的服务健康历史：探测时间、状态、延迟、错误类别、服务/图层计数，以及延迟趋势、成功率与状态历史——全部只基于本机记录。

## 便携备份（v0.6.0）

- 便携备份（`.apimonitor-backup`，ZIP+JSON）只包含账户非敏感元数据、Provider 非敏感设置（含 xAI Team ID）、余额历史、阈值与各类设置；**明确标记 containsSecrets=false**。
- **不导出**：API Key、Management Key、Credential Locker 内容、Authorization、日志、PFX、证书私钥、临时缓存、活动通知 Tag 与短期通知去重状态。
- 导入为安全合并：已有账户保留本机凭据；新账户标记“需要重新输入凭据”；历史按稳定 Id 去重；导入失败回滚。

## 手动更新检查（v0.6.0）

- 只在用户点击“检查更新”时访问 GitHub REST（`repos/KiYouJyo/ApiMonitor/releases/latest`），请求仅包含版本号（User-Agent）；**不上传账户、余额、历史或设备数据**。
- 发现新版本后由用户决定是否下载安装；应用不自动下载、不自动安装。

## 通知中心提醒（v0.5.0）

- 低余额与余额恢复通知**由本机 ApiMonitor 进程在运行期间生成**；不使用云端推送，不使用 WNS 远程推送，不经过任何第三方推送服务。
- v0.9.0 起还支持服务健康通知（凭据无效、权限不足、服务未启用、配额耗尽、服务不可用、服务恢复、预期服务/图层缺失与恢复）。瞬时错误连续两次后才通知，恢复一次即通知，手动测试失败不通知；新地图/GIS 账户默认关闭通知。
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

## Microsoft Store（准备中）

- 计划中的 Microsoft Store 版本与 GitHub 侧载版本在隐私行为上完全一致：无遥测、无广告、无云同步、无开发者服务器；凭据只保存在 Windows Credential Locker。
- Microsoft Store 版本将由 Store 完成签名与更新分发；在正式上架前，本应用仅通过 GitHub Releases 侧载分发。
- 本项目与任何被支持的 API 厂商均无官方隶属或合作关系。

## 联系我们

如有隐私相关问题，请通过仓库 [Issues](https://github.com/KiYouJyo/ApiMonitor/issues) 或 GitHub Private Vulnerability Reporting 联系我们，切勿在公开渠道粘贴真实 API Key。
