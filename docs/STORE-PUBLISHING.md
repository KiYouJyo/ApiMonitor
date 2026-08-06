# Microsoft Store 发布指南

本文档说明 ApiMonitor 的 Microsoft Store 渠道：正式身份、候选包构建、验证与提交边界。

## 正式身份（Partner Center，2026-08-06 核验）

| 字段 | 值 |
| --- | --- |
| 产品名称 | ApiMonitor |
| Product ID | `9N6KR2XFMKQ2` |
| Identity Name | `JoKiy.ApiMonitor` |
| Publisher | `CN=C4E4B33A-7B77-4121-897C-7D720A5471F8` |
| PublisherDisplayName | Jo Kiyō |
| Package Family | `JoKiy.ApiMonitor_4wdwgytaw3v2m` |
| PackageVersion | `1.0.0.0` |

GitHub 侧载身份（`ApiMonitor` / `CN=ApiMonitorDev`）与 Store 身份完全独立，绝不混用。

## 候选包构建

```powershell
packaging\New-StorePackage.ps1 -SourceCommit <HEAD> -PackageVersion 1.0.0.0
```

- 在隔离 worktree 中把 `Package.Store.appxmanifest` 复制为构建用 manifest。
- 生成未签名 `.msixupload`（Store 会重新签名），输出到 `packaging/output/v1.0.0/store/`。
- `Test-StorePackageIdentity.ps1` 验证 Identity/Publisher/Version/架构/三语/能力/禁止文件。
- 可选 `-CreateLocalTestPackage` 生成仅本机验收用的开发签名 MSIX（绝不上传）。

## 验证

- WACK（Windows App Certification Kit）在最终冻结候选上运行；报告保存到 `artifacts/wack/v1.0.0/`。
- 禁止文件：无 Install/Uninstall 脚本、无 CER/PFX/私钥、无 LocalState/日志/用户数据。

## 提交边界（2026-08-06 状态）

- Store 包 `1.0.0.0` 已上传至 Partner Center 首次提交草稿；尚未提交认证，也尚未公开发布。
- **已知限制**：msstore CLI（0.3.7.5）无法向 Partner Center 网页创建的草稿上传包。此类草稿需要在 Partner Center 网页的 Packages 页手动上传，或改用 CLI 创建的提交流程（独立授权步骤）。
- 上传、认证、发布是相互独立的授权步骤；任何脚本不得自动提交认证或发布。

## 用户手动补全清单

首次提交的信息补全（定价、市场、类别、年龄分级、三语商店说明、Logo/截图、隐私/支持 URL、认证备注等）按 `packaging/output/v1.0.0/store/PartnerCenter-Manual-Completion.md` 执行；CLI 不修改这些字段。
