# 数据备份

本文档说明 ApiMonitor 的便携备份（`.apimonitor-backup`）与安装器本地备份。

## 便携备份（应用内）

设置 → 数据管理 支持导出/导入 `.apimonitor-backup`（ZIP+JSON）。

**包含**：
- 账户非敏感元数据（含 xAI Team ID 等非敏感配置字段）
- 余额历史
- 阈值
- 自动刷新 / 通知 / 托盘 / 悬浮窗 / 外观（主题与语言）设置

**不包含**：
- API Key、Management Key、SK、Token 等任何凭据
- Credential Locker 内容、Authorization、日志、PFX、证书私钥
- 活动通知 Tag 与短期去重状态

**导入为安全合并**：
- 已有账户保留本机凭据
- 新账户标记“需要重新输入凭据”
- 历史按稳定 Id 去重
- 失败回滚

备份文件明确标记 `containsSecrets=false`。

## 安装器本地备份（升级前）

`Install.cmd` 升级前会尽力把 LocalState 备份到 `%TEMP%\ApiMonitor-LocalState-Backups`（备份失败不阻塞标准 MSIX 原地升级）。

- 备份清单只包含相对文件名、大小、SHA-256、备份时间、Package Family 与应用版本。
- 恢复（`packaging/tools/SafeLocalStateBackup.ps1` 的 `Restore-SafeLocalState`）会校验清单与哈希、确认 Package Family、二次备份当前数据、拒绝覆盖备份之外的新文件，并在恢复后复验 JSON。

> **Credential Locker 密钥无法通过文件备份恢复**：保护数据首选原地升级，而不是卸载重装。

## 渠道注意

Store 版与 GitHub 侧载版使用不同 Package Family；便携备份可在重新安装后导入（需要重新输入密钥），但两条渠道之间不存在自动数据迁移。
