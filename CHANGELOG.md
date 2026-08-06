# Changelog

## Unreleased

## 1.0.0（2026-08-06，已正式发布）

### Added

- 分发渠道模型：`DistributionChannel`（Development / GitHubSideload / MicrosoftStore），渠道在构建时通过 MSBuild 属性确定，禁止运行时猜测；关于页与诊断信息显示渠道、更新来源、包身份与架构
- Microsoft Store 正式渠道：Partner Center 官方身份 `JoKiy.ApiMonitor`（ProductId `9N6KR2XFMKQ2`），Store 构建使用独立 `Package.Store.appxmanifest` 与隔离输出目录 `packaging\output\v1.0.0\store`
- 按渠道的更新检查：`IUpdateService` + GitHubUpdateService / MicrosoftStoreUpdateService（StoreContext + 主窗口 HWND）/ DevelopmentUpdateService；Store 版绝不打开 GitHub 下载页
- 首次启动引导：四步（欢迎与隐私 / 添加第一个账户 / 运行方式 / 完成），可跳过、可重开，不自动开启通知、登录启动、自动刷新或关闭到托盘
- 应用运行状况检查：21 项只读非敏感检查（渠道、版本、包身份、架构、Credential Locker、数据文件、Provider、通知、托盘、登录启动、调度器、窗口、最近查询、更新服务匹配），支持重新检查、复制诊断、打开支持文档
- `store-package.yml` 手动工作流（仅 `workflow_dispatch`，无密钥，无 Partner Center 调用）
- Store 打包脚本 `New-StorePackage.ps1` / `Test-StorePackageIdentity.ps1` 与 GitHub 候选脚本 `New-GitHubCandidatePackage.ps1`

### Changed

- 用户可见版本 `1.0.0`；GitHub 侧载候选包版本 `1.0.0.1`；Store 正式包版本固定 `1.0.0.0`
- 版本与渠道配置集中在 `Directory.Build.props`；`AppInfo` 读取实际安装包身份
- 关于页新增分发渠道 / 更新来源 / 包系列 / 应用运行状况区域
- 设置页新增“重新打开首次使用引导”
- 三语资源扩展到 803 键；README / PRIVACY / docs 更新为 v1.0.0 状态

### Security / privacy

- Store 版按全新安装处理，不实现任何跨包数据迁移（无旧 PFN 检测、无旧 LocalState 读取、无 Credential Locker 跨包读取、无迁移向导）
- 运行状况与诊断信息只含非敏感元数据，不含 API Key、余额、账户名、完整路径或 Credential Locker Resource 明细
- 本地 Store 身份测试包使用与正式 Publisher 同名的本地测试证书签名，仅用于本机人工验收，绝不上传

### Compatibility

- GitHub 侧载版在 v0.9.0 之上原地升级，保留账户、凭据、历史与设置
- Microsoft Store 版为全新身份（`JoKiy.ApiMonitor_4wdwgytaw3v2m`），首次启动为空账户并显示引导
- 全部既有 Provider、账户模型与数据 schema（v3）保持不变

### Known limitations

- Microsoft Store 尚未正式上架：候选包、WACK 报告与商店资料已准备，等待人工验收后另行提交
- Partner Center 占位草稿未修改、未提交审核
- Store 版更新检查依赖 StoreContext 与 Store 服务可用性；离线或服务不可用时显示明确错误，不回退 GitHub

## [0.9.0] - 2026-08-05

### Added

- 新增 6 个 Provider：`amap`（高德 Web 服务）、`baidu-maps`（百度地图 Web API）、`tencent-location`（腾讯位置服务 WebService）、`tianditu`（天地图地名搜索 V2.0）、`supermap-iserver`（SuperMap iServer 服务目录）、`ogc-service`（通用 OGC WMS/WMTS/WFS）
- 通用指标模型扩展：`ProviderCategory` / `ProviderCapability` / `MetricKind` / `MetricValueKind` / `GeospatialStatus`；服务状态、延迟、计数与资金余额分开统计，地理/GIS 账户绝不进入余额汇总，未知配额保持 null
- 多槽位凭据：Key+SK、Basic 用户名+密码、Bearer Token、Query Token 独立存入 Credential Locker（`ApiMonitor` 资源不变）；账户 JSON 只保存存在标志，旧单密钥条目保持可读
- 服务健康通知：CredentialInvalid / PermissionDenied / ServiceNotEnabled / QuotaExceeded / ServiceUnavailable / ServiceRecovered / ExpectedServiceMissing / ExpectedServiceRecovered；瞬时错误连续两次后通知，恢复一次即通知，手动测试不通知
- 地理/GIS 历史与洞察：探测时间、状态、延迟、错误类别、服务/图层计数；延迟趋势、成功率、状态历史、计数变化；CSV 安全导出
- 配额保护：新地图账户默认关闭自动刷新，启用后默认 6 小时、最短 1 小时；429/配额/401/403/Key 无效不重试
- 安全 XML 解析（OGC）：禁用 DTD/外部实体/实体扩展，限制大小与深度，非 XML 安全失败

### Changed

- 首页新增分类筛选（全部 / AI 与模型 / 地图开放平台 / GIS 服务）与服务状态汇总（余额账户 / 服务正常 / 需要注意 / 查询失败）
- 地图/GIS 卡片显示服务状态、凭据状态、权限状态、配额状态、延迟、探测服务与“本次探测可能消耗 1 次 API 调用”
- 悬浮窗支持地理/GIS 账户（状态 + 延迟 + 错误，不再显示 ¥0/0 Credits）

### Security

- 地图 Provider 锁定官方 HTTPS 主机且不允许自定义 Base URL；不跟随重定向，凭据不跨 Origin 转发
- 自托管 GIS 仅允许 http/https（HTTP 需显式确认），拒绝 file/ftp/data/自定义协议；凭据不跨主机/端口/降级转发
- 日志剥离 `key/ak/tk/sig/sn/token` 等敏感查询参数；异常不含完整请求 URI；不抓取厂商控制台、不扫描局域网
- 备份与 CSV 仍不含任何凭据值；新增凭据槽位值也绝不进入备份

### Compatibility

- 在 v0.8.0 之上原地升级（同时兼容 v0.7.0 安装）：账户、AccountId、Credential Locker 条目（含新增多槽位凭据）、最新余额/历史、阈值、自动刷新 / 通知 / 托盘 / 悬浮窗 / 登录启动 / 外观（主题与语言）设置全部保留
- accounts/history JSON schema 保持 v3：新增字段全部可空（`credentialSlots`、指标值类型等），v0.8.0 / v0.7.0 文件无需迁移
- 既有 5 个 AI Provider 的 Provider ID 与 Metric ID 完全不变；地图/GIS 服务账户绝不进入资金余额汇总
- 安装器同版本保护与原地升级判断继续生效；GitHub 侧载版与 Microsoft Store 版分别构建、分别发布
- v0.8.0 未作为独立正式版本发布，其新增功能（Moonshot / SiliconFlow / xAI）随本版本一并交付

### Known limitations

- 高德、百度、腾讯、天地图不提供公开精确剩余配额接口，`quota.remaining/used/limit/reset_at` 保持 null，绝不伪造
- 天地图官方未公开 Token 无效/权限不足/调用超限的状态码；无法识别的状态码显示为带数值的安全 ProviderError，不做语义猜测
- 一次主动探测消耗一次 API 调用额度；新地图账户默认关闭自动刷新，启用后默认 6 小时、最短 1 小时
- MapGIS Server 通过通用 OGC Provider（WMS/WMTS/WFS GetCapabilities）监测，不提供未经验证的专有 `mapgis-server` Provider
- 自动刷新与通知只在 ApiMonitor 进程运行时执行；完全退出后不再查询、不再提醒
- Microsoft Store 版尚处于准备阶段（等待 Store 产品关联），GitHub 侧载版使用自签名 `CN=ApiMonitorDev` 开发证书

## [0.8.0] - 2026-08-05

### Added

- Moonshot / Kimi 余额 Provider（`moonshot`）：GET `https://api.moonshot.cn/v1/users/me/balance`，主指标可用余额（CNY，官方 `available_balance` = 现金 + 代金券），次级现金 / 代金券余额；缺失字段为 null
- SiliconFlow 余额 Provider（`siliconflow`）：GET `https://api.siliconflow.cn/v1/user/info`，主指标 `totalBalance`，次级 `balance` / `chargeBalance` / 可选 `grantedBalance`；忽略用户资料、完整响应不落日志、结构变化返回“响应结构暂不支持”
- xAI 余额 Provider（`xai`）：Management API `GET https://management-api.x.ai/v1/billing/teams/{team_id}/prepaid/balance`，需要 Management Key 与 Team ID（非敏感账户配置）；按官方“USD Cents + 账务方向”文档换算预付费 Credits，负值保留、不钳制、不 `Math.Abs`
- Provider 能力元数据：默认官方 Base URL、必填非敏感配置字段、主指标、币种、多币种 / 分项 / 凭据验证支持；官方 Provider 不允许自定义端点
- 凭据请求统一 HTTPS 主机白名单 + 超时 / 429 / 5xx 有限重试（可取消；401 / 403 / 404 不重试）
- 账户编辑器动态非敏感配置字段（xAI Team ID）；编辑中切换 Provider 清空密钥并要求重新测试，旧凭据不跨 Provider 沿用

### Changed

- Provider 注册表从 2 个扩展到 5 个（DeepSeek / OpenRouter / Moonshot / SiliconFlow / xAI）
- 账户数据 schema 保持 v3：新增可空 `providerConfig`（Team ID 等非敏感字段），v0.7.0 文件无需迁移
- 便携备份包含非敏感 Provider 配置（如 Team ID），仍绝不包含任何密钥

### Fixed

- 无

### Security

- 所有凭据请求发送前校验 HTTPS 与官方主机白名单；OpenRouter / xAI 的 Management Key 只发往对应官方主机
- 错误信息、诊断、备份与日志均不含 API Key、Management Key、Authorization、完整响应正文或用户资料

## [0.7.0] - 2026-08-04

### Added

- Floating balance window for a selected account
- Account-card command for selecting the floating account
- Persistent floating-window position and selected account
- Updated application icon assets

### Changed

- Replaced the former compact window with a smaller floating balance widget
- Simplified the home-page header
- Reduced the floating window to a fixed-size, single-surface design
- Changed the floating window to a monochrome visual style
- Updated window, taskbar, Start menu, package and notification-area icons

### Fixed

- Floating window showing a large outer background
- Mismatched inner and outer corner radii
- Severe lag while dragging the floating window
- Truncated account names, statuses, balances and currency units
- Old icon remaining in the main-window title bar
- Unused spacing remaining after removal of the home-page subtitle

> Historical note: the former compact window was replaced by the floating balance window.

## [0.6.0.1] - 2026-08-03（v0.6.0 主题统一修订包）

### Fixed

- 主题未实际应用：AppearanceSettingsViewModel 构造函数在读取持久化设置前把默认主题 System 写回文件，导致手动选择的 Light/Dark 永远无法恢复；改为构造期直接赋值后备字段
- 应用外壳背景不统一：标题栏（原生 AppWindowTitleBar 浅青）、NavigationView Pane（Acrylic 深色）、页面内容三处各色；新增统一语义资源（AppShellBackgroundBrush/AppCardBackgroundBrush 等，Light/Dark/HighContrast 三字典），NavigationView Pane 覆盖为外壳背景，页面根继承统一背景
- 新增 WindowThemeCoordinator：统一管理窗口根元素主题与原生标题栏颜色同步（含紧凑窗口），高对比度回退系统默认标题栏
- 卡片背景统一为 AppCardBackgroundBrush（设置/账户/关于页卡片），与外壳背景保持层级

## [0.6.0] - 2026-08-03

### Added

- 数据洞察页（导航主菜单）：账户/指标/时间范围选择、轻量本地趋势图（Canvas/Polyline，无图表框架）、当前值/区间变化/首末与极值、可折叠历史表、CSV 导出
- 消费估算服务：按连续快照计算日消耗中位数与预计可用天数，至少 3 个有效区间与 24 小时跨度；数据不足/未观察到消耗/最近充值/不支持指标/当前值未知等明确原因
- 便携备份（.apimonitor-backup，ZIP+JSON）：manifest（backupFormatVersion=1、文件大小/SHA-256、containsSecrets=false）、导出不含 API Key/凭据/日志、导入安全合并（保留本机凭据、新账户标记需要凭据、历史按 Id 去重、失败回滚）、路径穿越/超大文件拒绝
- 主题设置：跟随系统/浅色/深色，立即生效并应用到主窗口与紧凑窗口，持久化
- 三语支持：简体中文/English/日本語，x:Uid + ResourceLoader + 统一字符串服务；语言切换保存 PrimaryLanguageOverride 并提示重启（AppInstance.Restart）
- 完整“关于”页：产品信息（DisplayVersion 与 PackageVersion 分离）、当前能力（Provider 注册表动态）、隐私与安全摘要、项目链接、本地文档（离线查看）、手动检查更新（GitHub REST、15 秒超时、语义版本比较）、复制诊断信息（非敏感）、打开本地数据文件夹
- 集中版本来源 Directory.Build.props：DisplayVersion=0.6.0 / PackageVersion=0.6.0.0，不再“强制 major.minor.0”
- 统一元数据服务 AppInfo：打包/未打包安全回退，不因 Package.Current 不可用崩溃

### Changed

- 分离 DisplayVersion 与 PackageVersion（集中版本来源 Directory.Build.props）
- 数据洞察历史按需加载与图表分桶抽样（约 500 点，保留首末与极值）
- 统一标题栏、导航面板与页面背景（详见 0.6.0.1 修订条目）
- 高 DPI、高对比度与键盘无障碍改进
- 本地化扩展到对话框、托盘菜单、系统通知与 Provider 错误文案
- 导航结构：主页/数据洞察为主菜单，设置/关于移到底部区域；四页共享同一账户服务与状态，切换不重启调度器/不重复订阅/不重复读取 Credential Locker
- 托盘菜单与系统通知文本接入 IAppStrings 本地化
- Package.appxmanifest 声明 zh-CN/en-US/ja-JP 三种语言；版本升级到 0.6.0.0

### Fixed

- 修复了“查看趋势”入口：从账户卡片可进入数据洞察并自动选择该账户（以 AccountId 为主键）
- 修复缺失或混杂语言的界面字符串（x:Uid 失效，改用 Loc 附加属性本地化）
- 修复页面重叠回归与语言/主题状态显示
- 页面与对话框主题同步、外壳配色不一致（详见 0.6.0.1 修订条目）

### Security

- 便携备份明确 containsSecrets=false；CSV/备份/诊断信息不含 API Key、凭据、Authorization、日志与本机路径
- 更新检查仅手动触发、不上传任何账户/余额/设备数据、不自动下载安装、User-Agent=ApiMonitor/&lt;DisplayVersion&gt;

## [0.5.0.1] - 2026-08-03（v0.5.0 验收候选修订包；修复安装/备份安全）

- 禁止同版本破坏性替换：已安装版本与待安装版本相同时默认停止（退出码 15），绝不自动卸载/重置包/删除 LocalState/操作 Credential Locker
- 新增统一 LocalState 备份/校验/恢复工具（packaging/tools/SafeLocalStateBackup.ps1）：动态解析 Package Family、逐项 -LiteralPath 复制、数量/字节/哈希/JSON/非零/清单全量校验
- 破坏性重装仅在显式参数 -ForceDestructiveReinstall + 人工确认后执行，且必须先行通过备份校验（备份失败停止，退出码 16）
- 升级前尽力备份 LocalState；备份失败不阻塞安全的标准 MSIX 原地升级
- 候选包版本策略：MSIX 使用 0.5.0.1（后续 0.5.0.2/0.5.0.3…），应用界面与 GitHub 版本仍为 v0.5.0
- Install.ps1/Uninstall.ps1 退出码表补充 15/16；Installer 测试新增 39 项断言

## [0.5.0-UI] - 2026-08-03（验收前主界面信息架构修正，仍在 0.5.0.0）

- 主窗口改为 NavigationView 导航外壳：主页（账户仪表盘）与独立设置页
- 恢复醒目的“添加账户”入口（顶部主按钮 + 空状态“添加第一个账户”）
- 账户卡片恢复完整信息展示（指标/监测状态/通知状态/统一操作栏），修复卡片过矮裁切
- 汇总与筛选框增加明确标签；通知激活强制回到主页并定位账户
- 删除账户确认包含 Provider、删除范围与不可撤销提示

## [0.5.0] - 2026-08-03

### Added

- 通用余额指标模型（BalanceMetric）：货币余额、平台 Credits、密钥额度、累计/周期使用量统一表示；未知数值为 null，无限额度不误触发低余额提醒
- 多账户管理：同一 Provider 可添加多个账户；主界面账户总数/低余额/查询失败汇总、Provider 与状态筛选、刷新全部账户
- OpenRouter Provider：普通 API Key（/api/v1/key）与 Management Key（/api/v1/credits）两种凭据模式，剩余 Credits = 总充值 − 总使用（负数不钳制）；凭据模式为账户非敏感设置，密钥仍只进 Credential Locker
- Windows 通知中心低余额提醒（AppNotification）：首次低余额、重复提醒冷却（不重复/6h/12h/24h/3d）、余额恢复提醒、暂停提醒 24 小时、快照去重、多指标合并、稳定 Tag 替换、测试通知
- 全局与每账户通知设置：升级后全局系统提醒默认关闭；已有 DeepSeek 阈值保留
- 通知注册与单实例激活整合：先绑定 NotificationInvoked 再 Register；通知点击打开/定位对应账户，第二实例重定向，退出时 Unregister
- 数据迁移：账户/余额文件升级到通用指标结构（账户 schema 2→3、余额记录 schema 2→3、设置体系 tray-settings 4→5），迁移前备份旧文件且幂等
- 安装与卸载工具升级到 0.5.0.0，支持 v0.4.0 原地升级，安装脚本不自动开启通知与登录启动

### Changed

- 余额快照与历史记录改用稳定 MetricId；阈值规则按 MetricId 关联
- Package.appxmanifest 增加 windows.toastNotificationActivation 与 comServer 扩展（AppNotification 激活）
- 主界面、紧凑窗口、托盘状态与通知状态共用同一套阈值计算

### Fixed

- 修复了升级后凭据模式变化会误要求重新测试连接的问题

### Security

- 通知参数只包含 action/accountId/providerId/metricId 非敏感标识；通知内容不含 API Key
- OpenRouter Management Key 权限说明与 403 提示；`limit_remaining=null` 显示为无限额度而非 0

## [0.4.0] - 2026-08-03

## [0.4.0] - 2026-08-03

### Added

- Native Windows notification-area icon (Shell_NotifyIconW, NOTIFYICON_VERSION_4, stable GUID, dedicated hidden message window)
- Close-to-tray behavior: closing the main window hides it to the notification area (configurable to exit instead)
- Tray menu for opening the main and compact windows, refreshing all accounts, toggling sign-in startup and exiting
- Single-instance activation: a second launch redirects to and activates the existing instance
- Optional start at Windows sign-in (MSIX StartupTask, off by default; sign-in startup stays in the tray only)
- Explorer restart recovery: the tray icon reappears automatically after Explorer restarts
- Tray Tooltip balance-status summary (normal / low-balance count / no data / refreshing / recent failure), without API keys
- Persistent notification-area and startup settings (tray-settings.json, schema 3 → 4)
- Multi-size tray icon (16/20/24/32/48/256)
- In-place upgrade from v0.3.1 preserving accounts, balance history, thresholds, compact-window settings and Credential Locker keys

### Fixed

- Fixed the tray context menu appearing near the upper-left corner
- Corrected NOTIFYICON_VERSION_4 callback parsing (event in LOWORD(lParam), icon ID in HIWORD(lParam), cursor anchor from GetCursorPos / wParam)
- Positioned the context menu near the cursor or the notification icon, with monitor-aware expansion direction
- Fixed reopening a hidden main window from the tray menu

### Changed

- Application lifecycle now remains active while the tray icon is running
- Closing the final visible window no longer necessarily exits the application
- Explicit exit now performs centralized cleanup (scheduler, in-flight requests, settings, tray icon, windows)
- Settings schema upgraded for tray and startup preferences

## [0.3.1] - 2026-08-03

- 新增一键侧载安装体验：双击 `Install.cmd`，确认一次 UAC 后自动完成证书导入、依赖检查与 MSIX 安装/升级
- 安装程序自动校验 SHA-256、MSIX 签名证书完整 Thumbprint、证书 Subject/EKU/有效期与包 Identity
- 证书只导入 Local Machine\TrustedPeople，绝不导入 Trusted Root；同 Thumbprint 已存在时跳过
- 支持 v0.3.0 → v0.3.1 原地升级，保留账户、余额历史、阈值、窗口设置与 Credential Locker 凭据
- 相同版本不重复安装；更高版本拒绝降级；同名不同 Publisher 的冲突包明确报错
- 依赖仅安装当前 x64 系统所需；已安装相同或更高版本时跳过
- 新增 `Uninstall.cmd`：仅卸载当前用户的 ApiMonitor（精确 Identity 匹配，不使用 -AllUsers），可选择按完整 Thumbprint 清理证书
- 安装/卸载日志写入 `%TEMP%`，不包含 API Key、Credential Locker 内容或余额数据
- 新增隔离临时目录运行安装工具测试（69 项），CI 中不修改机器级证书库、不安装真实包
- 更新 README、README.zh-CN、SUPPORT 与安装/卸载文档

## [0.3.0] - 2026-08-03

- 完成内部名称、包身份和开发签名身份的统一
- 新增紧凑余额窗口
- 支持始终置顶
- 支持账户与币种选择
- 主窗口和紧凑窗口状态同步
- 保存窗口位置、尺寸和置顶设置
- 多显示器与屏幕外位置恢复
- MIT License 与仓库维护完善
- 不再兼容旧包身份和旧本地数据（v0.2.0 无法原地升级；账户、余额历史与 API Key 不迁移）

## v0.2.0

发布日期：2026-08-03

### 新增

- 产品更名为 **ApiMonitor**
- 应用运行期间自动刷新余额
- 本地余额历史记录
- 按币种设置低余额阈值规则
- 自动刷新状态显示（开启/关闭、间隔、下次刷新时间）
- API Key 一键安全复制（写入剪贴板，约 30 秒后尝试清理）
- 未打包数据目录与 Credential Locker 资源统一使用 ApiMonitor 标识

### 修复

- “最近成功更新”时间绑定不再显示 `{x:Bind ...}` 文本
- 账户卡片“可用状态”在刷新后实时更新
- 添加/编辑账户对话框外框与按钮圆角统一
- 低余额阈值区域在无余额数据时显示明确提示，测试连接后同步币种余额
- 主界面版本号改为从程序集动态读取，避免与包版本不一致

### 兼容性

- 兼容读取 v0.1.0（schemaVersion 1）数据并迁移到 v0.2.0（schemaVersion 2）
- 保留 MSIX Identity、发布者、开发证书与包族，支持覆盖更新

## v0.1.0

发布日期：2026-07-30（约）

- 初始 DeepSeek Provider
- API Key 保存到 Windows Credential Locker
- 手动余额查询
- 账户添加、编辑与删除
- 本地余额快照

