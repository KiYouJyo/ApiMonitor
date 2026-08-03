# ApiMonitor v0.4.0 卸载说明（Uninstall.cmd）

## 普通卸载

1. 双击解压目录中的 **`Uninstall.cmd`**。
2. 脚本只卸载**当前用户**的 `ApiMonitor`（精确匹配包 Identity，不使用 `-AllUsers`，也不会删除其他应用）。
3. 如果 ApiMonitor 正在运行，脚本会先请求正常关闭；无法关闭时会询问是否强制结束。
4. 卸载完成后脚本会询问“是否同时移除 ApiMonitor 自签名开发证书？”，输入 `Y` 才会执行证书清理，默认保留。

> 卸载会移除应用本体；本地账户、余额历史、阈值、窗口设置与 Credential Locker 凭据可能不再可用于该包。卸载程序不承诺保留这些数据。删除的包数据不可恢复。

## 完整卸载（同时删除证书）

高级用户可以在**管理员 PowerShell** 中执行：

```powershell
.\Uninstall.ps1 -RemoveCertificate
```

或双击 `Uninstall.cmd` 后在证书询问处输入 `Y`。

证书清理规则：

- 只删除 **Local Machine > Trusted People** 中 Thumbprint 完全匹配的证书；
- 删除前检查**所有用户**是否仍存在 Publisher 为 `CN=ApiMonitorDev` 的已安装包，若仍存在则**不删除**证书并说明原因；
- 不删除 Trusted Root、Personal 或其他位置的同名证书，也不按 Subject 批量删除。

## 多用户电脑上的限制

- 默认卸载只影响当前用户；其他用户安装的 ApiMonitor 不受影响。
- 证书是机器级（Local Machine）的。只要**任意用户**仍有 `CN=ApiMonitorDev` 签发的包，证书清理就会被阻止，以保证其他用户的应用可以继续启动。
- 需要管理员权限的步骤（证书清理）会触发一次 UAC。

## 数据和 API Key 注意事项

- ApiMonitor 的 API Key 存放在 **Windows Credential Locker**，卸载包不会主动删除凭据项，但重新安装后应用只能读取其自己的凭据资源。
- 账户 JSON、余额历史与设置位于应用 LocalState 中；原地升级（v0.3.1 → v0.4.0）会保留这些数据，卸载则不会保留。
- 不要在任何日志、Issue 或聊天中粘贴真实 API Key。

## 退出码

| 退出码 | 含义 |
| --- | --- |
| 0 | 卸载成功（含证书清理完成或选择保留证书） |
| 10 | 当前用户未安装 ApiMonitor |
| 11 | 卸载失败或卸载后验证失败 |
| 12 | 用户取消（应用运行中拒绝强制结束） |
| 13 | 证书清理被阻止（仍有 CN=ApiMonitorDev 包存在） |
| 14 | 证书清理失败 |
| 2 | 用户取消 UAC |
