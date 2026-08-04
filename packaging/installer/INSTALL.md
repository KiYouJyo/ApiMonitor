# ApiMonitor v0.7.0 安装说明（Install.cmd，候选包版本 0.7.0.0）

ApiMonitor v0.7.0（候选包版本 `0.7.0.0`）是自签名侧载版本。完整 `Test.zip` 解压后，普通用户只需：

1. **下载完整 Test.zip**（`ApiMonitor_0.7.0.0_x64_Test.zip`）并解压到任意目录（路径可包含空格和中文）。
2. **双击 `Install.cmd`**。
3. **确认一次 Windows UAC 提示**（“用户帐户控制”，选择“是”）。
4. 等待脚本自动完成证书导入、依赖检查与 MSIX 安装。
5. 安装成功后脚本会询问是否立即启动 ApiMonitor，输入 `Y`（默认）即可。

> 安装/升级**不会**自动开启“登录 Windows 时启动”，也**不会**自动开启余额系统提醒；两者默认关闭，只能由你在应用设置中主动启用。

不需要手动打开证书管理器、手动输入 PowerShell 命令、修改执行策略或启用开发人员模式。

## 为什么会出现一次 UAC？

自签名侧载证书必须写入 **本地计算机 > 受信任人（Local Machine > Trusted People）**，该位置需要管理员权限。安装程序使用标准的 Windows UAC 机制请求提升，脚本不会绕过 UAC，也**不会**把证书导入受信任根证书颁发机构（Trusted Root）。

## SmartScreen 可能出现什么提示？

- 双击 `Install.cmd` 时，SmartScreen/浏览器可能提示“Windows 已保护你的电脑”或“无法验证发布者”。
- 这是因为自签名证书不是商业 CA 签发的，属于正常现象。请确认文件来自官方 GitHub Releases 页面（KiYouJyo/ApiMonitor），必要时选择“仍要运行”/“更多信息 > 仍要运行”。
- 如果你不信任来源，请停止安装并删除文件。

## 安装程序做了什么

1. 检查 Windows 10 1809（build 17763）+、x64 系统以及所需文件（MSIX、CER、SHA256SUMS.txt）。
2. 校验 `SHA256SUMS.txt` 中 MSIX 与 CER 的 SHA-256。
3. 从 MSIX 提取签名证书，与随包 CER 的**完整 Thumbprint** 比对；核对 Subject 为 `CN=ApiMonitorDev`、代码签名 EKU、有效期、Manifest Publisher 与 Identity（`ApiMonitor` / 0.7.0.0）。
4. 将公开证书导入 **Local Machine > Trusted People**（已存在相同 Thumbprint 时跳过）。
5. 检查 `Dependencies\x64` 中的 Windows App Runtime 2 依赖，只安装当前 x64 系统需要的包；已安装相同或更高版本时跳过。
6. 全新安装或**原地升级**（v0.6.0 → v0.7.0.0），保留本地账户、历史、阈值、悬浮窗/托盘/启动设置与 Credential Locker 数据。

## 同版本保护

- 检测到已安装版本与待安装版本**完全相同**时，安装程序默认停止并提示：

  “已安装相同版本。请生成更高修订号的候选包，不要通过卸载重装替换。”

- 安装程序**不会**自动卸载当前应用、删除 LocalState、重置包或操作 Credential Locker。
- 同一 v0.7.0 的验收候选包依次使用 0.7.0.0、0.7.0.1、0.7.0.2 等修订号；用户可见版本始终为 **v0.7.0**，GitHub 标签仍为 v0.7.0（未发布前不打标签）。
- 破坏性重装只有在显式参数 `-ForceDestructiveReinstall` 并确认后才能执行：脚本会先用统一备份工具校验备份 LocalState，再卸载重装，并明确提示凭据风险；**正式发布流程不使用该参数**。

## 备份与恢复

- 安装程序升级前会尽力把 LocalState 备份到 `%TEMP%\ApiMonitor-LocalState-Backups`（备份失败不阻塞安全的标准 MSIX 原地升级）。
- 备份清单只包含相对文件名、大小、SHA-256、备份时间、Package Family 与应用版本，不含任何密钥内容。
- 恢复命令（`packaging\tools\SafeLocalStateBackup.ps1` 的 `Restore-SafeLocalState`）会先校验清单与哈希、确认 Package Family、二次备份当前数据、拒绝覆盖备份之外的新文件，并在恢复后复验 JSON。

> **Credential Locker 密钥无法通过 LocalState 文件备份恢复，因此保护数据的首选方式是原地升级，而不是卸载重装。**

安装日志写入 `%TEMP%\ApiMonitor-Install-*.log`，日志不包含 API Key、Credential Locker 内容或余额数据。

## 常见错误与退出码

| 退出码 | 含义 | 处理方法 |
| --- | --- | --- |
| 0 | 安装成功（或已安装当前版本） | 无需处理 |
| 2 | 用户取消（未同意 UAC） | 重新运行 Install.cmd 并同意 UAC |
| 4 | 已安装更高版本，拒绝降级 | 保留现有版本，或等新版本发布后再升级 |
| 5 | 同名但 Publisher 不同的包冲突 | 按提示处理冲突包后重试 |
| 6 | 安全校验失败（哈希/证书/Thumbprint/Manifest 不符） | 重新下载完整 Test.zip 并核对 SHA-256；不要使用来源不明的文件 |
| 7 | 前置检查失败（系统/文件缺失/多份 MSIX） | 检查系统版本与解压完整性 |
| 8 | 缺少依赖且系统中没有 Windows App Runtime | 使用包含 Dependencies 的完整 Test.zip，或从官方渠道安装 Windows App Runtime |
| 9 | MSIX 安装/升级失败 | 查看日志中的 0x 错误码；确认应用未锁定或磁盘空间充足 |
| 15 | 已安装相同版本，默认停止 | 生成更高修订号的候选包（如 0.7.0.0），不要卸载重装 |
| 16 | 破坏性重装前的 LocalState 备份失败 | 已停止，不会卸载；先解决备份失败原因 |
| 1 | 其他错误 | 查看安装日志 |

## 手动安装备用方案

如果希望手动完成（不推荐，仅作备用）：

```powershell
# 1. 以管理员身份运行 PowerShell，导入公开证书（仅 Trusted People）
Import-Certificate -FilePath .\ApiMonitorDev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# 2. 安装依赖（如未安装 Windows App Runtime 2）
Add-AppxPackage -Path .\Dependencies\x64\Microsoft.WindowsAppRuntime.2.msix

# 3. 安装主包
Add-AppxPackage -Path .\ApiMonitor_0.7.0.0_x64.msix
```

## SHA-256 校验方法

在解压目录或 Release 资产目录执行：

```powershell
Get-FileHash .\ApiMonitor_0.7.0.0_x64.msix -Algorithm SHA256
Get-FileHash .\ApiMonitor_0.7.0.0_x64_Test.zip -Algorithm SHA256
```

将结果与官方 Release 上的 `SHA256SUMS.txt` 逐字符比对。不一致时请勿安装。
