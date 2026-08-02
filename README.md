# ApiBalanceMonitor

ApiBalanceMonitor 是一个独立的 WinUI 3 桌面应用，用于查询并记录用户自己的 API 账户余额。

当前版本 `v0.1.0`（阶段一）：

- 支持 DeepSeek 官方余额接口（`GET https://api.deepseek.com/user/balance`）
- API Key 通过 Windows Credential Locker（`PasswordVault`）安全保存
- 普通配置与最近余额快照以 JSON 保存在应用专属本地目录
- 简洁的 Windows 11 Fluent 主界面：无账户引导、账户卡片、手动刷新、编辑、删除
- 添加/编辑账户对话框内置“测试连接”，测试成功只预览结果，点击保存才写入账户与凭据
- 独立 xUnit 测试项目，覆盖 Provider 解析、注册表、本地持久化与 ViewModel 状态

## 技术栈

- C# / .NET 10（`net10.0-windows10.0.26100.0`）
- WinUI 3 / Windows App SDK 2.3.1
- 单项目 MSIX 打包（x64）
- `System.Text.Json`、`HttpClient`
- CommunityToolkit.Mvvm 8.4.0（轻量 MVVM：`ObservableObject`、`AsyncRelayCommand`）

## 项目结构

```text
ApiBalanceMonitor.csproj     # 主应用（单项目 MSIX）
Models/                      # 领域模型（账户、余额快照、查询结果）
Providers/                   # IApiBalanceProvider、DeepSeek、注册表
Services/                    # 凭据/账户/快照存储、HTTP、组合根、对话框
ViewModels/                  # MainViewModel、AccountEditorViewModel 等
Views/                       # MainPage、AccountEditorDialog、转换器
tests/ApiBalanceMonitor.Tests/  # xUnit 测试
```

## 构建与测试

```powershell
dotnet restore ApiBalanceMonitor.slnx -p:Configuration=Release -p:Platform=x64
dotnet test ApiBalanceMonitor.slnx -c Release -p:Platform=x64 --no-restore
dotnet build ApiBalanceMonitor.slnx -c Debug -p:Platform=x64 --no-restore
dotnet build ApiBalanceMonitor.slnx -c Release -p:Platform=x64 --no-restore
```

输出目录：`bin/<Configuration>/net10.0-windows10.0.26100.0/win-x64`。

## 隐私

详见 [PRIVACY.md](PRIVACY.md)。API Key 只保存在 Windows 安全凭据存储中，
不写入 JSON、日志或任何导出数据。

## 人工验收要点（v0.1.0）

1. 无账户时显示用途说明、隐私说明与“添加 DeepSeek 账户”按钮。
2. 添加账户：输入名称与 API Key，先“测试连接”看到币种与总余额，再保存。
3. 编辑已有账户：不回填 API Key，显示“已保存凭据”，留空则沿用原密钥。
4. 手动刷新显示进度环，成功后更新币种余额与最近成功时间。
5. 删除账户时同时删除凭据与本地余额快照。
6. 重启应用后账户与最近余额快照仍存在。
7. 关闭应用后无后台进程残留（阶段一不含托盘/后台/通知）。
