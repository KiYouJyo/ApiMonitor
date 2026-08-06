# 发布指南

本文档是今后发布 ApiMonitor 的可复用指南。当前公开版本为 `1.0.0`；GitHub 侧载包使用 `1.0.0.2`，Microsoft Store 包使用独立的 `1.0.0.0`，两者身份与更新流程相互独立。

## 产品版本

- 用户可见版本：`1.0.0`（由 `Directory.Build.props` 的 `ApiMonitorDisplayVersion` 集中维护）。
- GitHub 侧载包版本：独立、单调递增的四段版本（`1.0.0.1`、`1.0.0.2` …），同一用户可见版本可有多份验收候选。
- Store 包版本：独立、单调递增，第四段为 `0`（当前 `1.0.0.0`）。
- 版本/渠道配置集中在 `Directory.Build.props`；渠道在构建时通过 `DistributionChannel` 属性确定，禁止运行时猜测。

## 通用准备

- 从最终 `main` 重新构建，记录提交与完整 SHA-256。
- 不要复用不同提交产生的候选包；不要把证书、私钥、PFX、MSIX 或本机验收文件提交仓库。
- Store 包上传/发布后，不要为同一版本随意重建；新版本必须提高对应渠道的包版本。

## GitHub 侧载渠道

1. 使用 `Package.appxmanifest`（Identity `ApiMonitor` / Publisher `CN=ApiMonitorDev`）。
2. `packaging/New-GitHubCandidatePackage.ps1 -PackageVersion <修订号>` 构建签名 MSIX 并组装 `Test.zip`（含 Install/Uninstall、公开 CER、依赖、SHA256SUMS）。
3. 验证 `signtool verify /pa`、包身份/版本/架构、Test.zip 内 SHA256SUMS 与内容一致。
4. 创建普通 latest Release，上传 `Test.zip`、`.msix` 与 `SHA256SUMS.txt`。
5. GitHub 更新检查只在用户点击时访问 Releases API。

## Microsoft Store 渠道

1. 使用 `Package.Store.appxmanifest`（Identity `JoKiy.ApiMonitor` / Publisher `CN=C4E4B33A-7B77-4121-897C-7D720A5471F8` / PFN `JoKiy.ApiMonitor_4wdwgytaw3v2m` / Product ID `9N6KR2XFMKQ2`）。
2. `packaging/New-StorePackage.ps1 -SourceCommit <HEAD> -PackageVersion 1.0.0.0` 在隔离 worktree 中构建未签名 `.msixupload`，验证身份/版本/内容并生成报告。
3. 在最终冻结产物上运行 WACK（Store 候选）；保存 XML/HTML 报告。
4. 上传 Partner Center 草稿并完成认证与发布（独立授权步骤）。

**已知限制（2026-08-06）**：当前 msstore CLI（0.3.7.5）无法向 Partner Center 网页创建的草稿上传包（返回“We can't upload the packages for submissions created in Partner Center”）。此类草稿的包上传需要在 Partner Center 网页的 Packages 页手动完成，或由 CLI 创建的提交流程完成。

## 构建与测试

```powershell
dotnet restore ApiMonitor.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet test tests\ApiMonitor.Tests\ApiMonitor.Tests.csproj -c Debug
dotnet build ApiMonitor.slnx -c Debug -p:Platform=x64 --no-restore
dotnet build ApiMonitor.csproj -c Release -p:Platform=x64 -p:DistributionChannel=GitHubSideload
dotnet build ApiMonitor.csproj -c Release -p:Platform=x64 -p:DistributionChannel=MicrosoftStore
powershell -NoProfile -ExecutionPolicy Bypass -File tests\installer\Installer.Tests.ps1
```

发布前还应完成：三语资源键集检查、Markdown 相对链接检查、`dotnet format --verify-no-changes`、GitHub 资产哈希与签名检查、敏感信息扫描、WACK（Store 渠道）与人工验收。

## 公开发布原则

- 从最终主线提交构建并保留构建记录与 SHA-256。
- GitHub Release 只上传经过确认的公开资产，不上传私钥、Store 上传包、WACK 报告、截图源文件或本机数据。
- 发布说明准确区分产品版本、两条渠道的包版本与独立身份，并说明 Store 认证状态。
- 发布、标签、合并和 Store 上传是相互独立的授权步骤。
