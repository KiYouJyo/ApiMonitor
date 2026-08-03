# Support

## Common installation issues

### Self-signed certificate

Since v0.3.1, `Install.cmd` performs all certificate steps automatically. The public certificate (`ApiMonitorDev.cer`) is imported only into **Local Machine > Trusted People** (never Trusted Root), and only after the script verifies the SHA-256 checksums, the full signer thumbprint, the Subject (`CN=ApiMonitorDev`), the Code Signing EKU, and the package Identity. You still see one normal UAC prompt because machine-level certificate trust requires administrator consent. Only install certificates you trust and only from the official repository.

### Upgrading v0.3.1 to v0.4.0

v0.4.0 upgrades **in place** over v0.3.1: accounts, balance history, thresholds, compact-window settings, and Credential Locker API keys are preserved. Just run `Install.cmd` again. The installer never enables the "start with Windows" startup task automatically.

### Upgrading v0.4.0 to v0.5.0

v0.5.0 upgrades **in place** over v0.4.0: accounts, AccountIds, Credential Locker API keys, latest balances, history, thresholds, auto-refresh settings, compact-window settings, tray settings, and sign-in startup preferences are preserved. Existing DeepSeek currency balances and thresholds are migrated losslessly to the generic metric model. The installer never enables notifications or sign-in startup automatically; global system alerts are **off by default** until you turn them on.

### Upgrading v0.5.0 to v0.6.0

v0.6.0 upgrades **in place** over v0.5.0: accounts, AccountIds, Credential Locker API keys, latest balances, history, thresholds, auto-refresh / notification / tray / compact-window / sign-in startup / appearance (theme and language) settings are all preserved. Just run `Install.cmd` from the v0.6.0 `Test.zip` again. Do **not** uninstall and reinstall for a normal upgrade — that would destroy LocalState and remove Credential Locker entries.

### Portable backup import

A v0.6.0 portable backup (`.apimonitor-backup`) never contains API keys or Management Keys. During import, existing accounts keep their local credentials; **new accounts are marked as needing a re-entered key** — edit the imported account and save its API key (or Management Key) in the app, then test the connection. If a needed key is missing, queries for that account fail with a clear “需要重新输入凭据” prompt.

### Candidate package revisions (0.5.0.1, 0.6.0.1, …)

- The user-visible version stays **v0.5.0** / **v0.6.0**; only the MSIX four-part version advances for each acceptance candidate.
- **The installer refuses a same-version install by default**: “已安装相同版本。请生成更高修订号的候选包，不要通过卸载重装替换。” It never auto-uninstalls, never removes LocalState, never resets the package, and never touches Credential Locker.
- If a destructive reinstall is truly required, use the explicit `-ForceDestructiveReinstall` parameter: the installer validates a LocalState backup first, warns about the credential risk, and only then uninstalls and reinstalls. This is not part of the formal release flow.

### Backups and Credential Locker

- Installer upgrades create a best-effort LocalState backup under `%TEMP%\ApiMonitor-LocalState-Backups`; the backup manifest contains only relative file names, sizes, SHA-256 hashes, backup time, Package Family, and app version — never API keys.
- The restore function (`Restore-SafeLocalState`) validates the manifest and hashes, checks the Package Family, creates a second backup of current data, refuses to overwrite unrecognized new files, and re-verifies JSON afterwards.
- **Credential Locker 密钥无法通过 LocalState 文件备份恢复，因此保护数据的首选方式是原地升级，而不是卸载重装。**

### Upgrading v0.3.0 to v0.3.1

v0.3.1 upgraded in place over v0.3.0 (historical release line).

### Upgrading from v0.2.0 to v0.3.0

v0.3.0 uses a new package identity and does not upgrade over a v0.2.0 sideload package. Accounts, balance history, and Credential Locker API keys are not migrated.

1. Uninstall the old test package: `Get-AppxPackage -Name ApiMonitor | Remove-AppxPackage`.
2. Install the new package with `Add-AppDevPackage.ps1` (or `Add-AppxPackage -Path <file>.msix`).
3. Add your accounts and API keys again inside the app.

Never paste real API keys into issues or logs during this process.

## Notification-area (tray) operation

Starting with v0.4.0 the app stays resident in the notification area:

- **Cannot find the tray icon?** It is the ApiMonitor blue "A" icon. Check the **overflow area** of the notification area (the `^` chevron near the clock) — Windows may have hidden it there; drag it out to pin it.
- **The tray icon disappeared after an Explorer restart?** It should reappear automatically within a few seconds (the app listens for the `TaskbarCreated` message and re-adds the icon). No restart of ApiMonitor is required. If it does not return, restart Explorer (`explorer.exe`) or sign out and back in.
- **How to fully exit the app?** Right-click the tray icon → **退出 ApiMonitor** (Exit ApiMonitor). Closing the main window only hides it to the tray by default; choose **关闭主窗口时：退出 ApiMonitor** in the settings to make the close button exit the app instead.
- **How to reopen the main window?** Left-click the tray icon, or right-click the tray icon → **打开 ApiMonitor**. You can also launch ApiMonitor again from Start (it activates the existing instance instead of starting a second one).
- **The tray context menu opens in the wrong place?** The menu is anchored to your cursor and expands toward the notification icon on the correct monitor. If it appears near the upper-left corner, your system may be using an older build; update to v0.4.0 (which contains the fix) and restart the app. If the issue persists, file an issue with your Windows version and taskbar position (bottom/top/left/right) and DPI scaling — do not paste API keys or logs containing them.
- **How to turn off start-with-Windows?** Open the main window → settings section **通知区域与启动** → toggle **登录 Windows 时启动 ApiMonitor** off. You can also disable it in **Task Manager → Startup apps** or **Settings → Apps → Startup**; the app respects that choice.
- **How to re-enable it in Windows startup settings?** Toggle it on inside the app, or enable `ApiMonitor` in **Settings → Apps → Startup**.
- The tray Tooltip shows a short balance summary (normal / low balance count / no data / refreshing). It never shows API keys or balance details.

## Windows notification-center (low-balance) alerts

Starting with v0.5.0, low-balance and recovery alerts use the Windows App SDK AppNotification API. Alerts are generated **locally by the running ApiMonitor process**; there is no cloud push.

### Alerts do not appear

- **Check that the alert is enabled**: open the main window → **余额提醒** section → enable **启用余额系统提醒** (off by default after upgrade), and make sure the account's own notification setting is not **关闭**.
- **Check the threshold**: the alert fires only when a successful query produces a balance below an enabled threshold for that metric. Historical snapshots from before the app started never trigger alerts.
- **Check Windows notification settings**: open **Settings → System → Notifications** (or press the **打开 Windows 通知设置** button in the app) and make sure notifications for ApiMonitor are allowed, **Focus assist / Do not disturb** is not blocking them, and the **勿扰/专注助手** hours are not active. ApiMonitor cannot bypass Windows notification settings and does not change them automatically.
- **Use the test notification**: the **发送测试通知** button sends a clearly labeled test notification without querying any API, changing threshold state, or writing balance history. If it does not appear, the cause is almost always a Windows-side setting (see above).
- **Quiet hours / Focus assist**: Windows may suppress notifications during Focus assist hours or when notifications are set to "Do not disturb". Check the action center.
- **App must be running**: alerts are only produced from queries made while ApiMonitor is running. Sign-in startup and tray residency keep it monitoring; choosing "退出 ApiMonitor" stops all queries and new alerts.
- If the notification appeared once and a low balance persists, the repeat interval (default 24 hours) prevents frequent repeats; choose a shorter interval or **暂停提醒 24 小时** behavior in the account's notification settings.

### "暂停提醒 24 小时" button

The button snoozes that account/metric for 24 hours without opening the app window, keeps the low-balance state, and does not clear the alert. Recovery alerts can still be sent according to your settings.

## OpenRouter credential modes

- **普通 API Key** (API Key mode) queries `https://openrouter.ai/api/v1/key`: it shows key quota remaining/limit and total/daily/weekly/monthly usage. It never claims to read the full account balance.
- **Management Key** (Management Key mode) queries `https://openrouter.ai/api/v1/credits`: it shows account Credits (remaining = total − usage). It has higher permissions than a normal API key and should only be used when you need account-level Credits.
- **403 when using a normal API key on the Credits endpoint**: OpenRouter returns 403 because that key is not a Management Key. Edit the account, switch the credential mode to **Management Key**, and save the Management Key.
- **`limit_remaining = null`**: means "no key quota is set, or the quota is not constrained by this field" — it is displayed as **无限额度** and never treated as 0, and it never triggers a low-balance alert.
- Keys are never sent to two endpoints automatically; the endpoint is chosen strictly by the selected credential mode. ApiMonitor never requests, creates, deletes, or rotates OpenRouter API keys, and never manages your OpenRouter account.

## Multi-account credential storage

- Each account's credential is associated with its AccountId in the Windows Credential Locker (resource `ApiMonitor`); Provider and credential mode are stored as plain account settings and never embedded in the key text.
- If saving a new account fails (for example the system reaches a Credential Locker limit), the app rolls back the new credential write so existing accounts are never damaged; a clear message is shown.
- If deleting an account fails partway, the app never silently leaves inconsistent state; retry and check the log for the exact failure.
- The UI shows how many accounts are configured but does not claim a fixed credential-count limit that applies to every Windows configuration.

## Recovering after an accidental uninstall/reinstall

- If a candidate package was replaced by uninstall/reinstall and LocalState was lost, restore the latest validated backup with `Restore-SafeLocalState` (see `packaging\tools\SafeLocalStateBackup.ps1`). Credential Locker keys are **not** stored in LocalState and cannot be restored from these files.

## Balance query issues

### Windows App Runtime

The app requires Windows App Runtime 2.3.1 or later. The full test package includes the runtime dependencies under `Dependencies\x64`, and `Install.cmd` installs only what the current x64 system is missing (same or higher installed versions are skipped). If Windows still reports a missing runtime, install the official Microsoft Windows App Runtime redistributable.

### MSIX installation fails

- Check the exit code shown by `Install.cmd` and the log file `%TEMP%\ApiMonitor-Install-*.log`; the code mapping is documented in `INSTALL.md`.
- Exit code 6 means a security check failed (checksum/certificate/thumbprint mismatch): re-download the full `Test.zip`, verify the SHA-256 values, and never install files from unknown sources.
- Exit code 4 means a newer version is already installed; the installer refuses to downgrade.
- Exit code 5 means another package with the same name but a different Publisher exists; resolve that conflict first.
- For manual troubleshooting, see the manual fallback in `INSTALL.md`.

## Balance query issues

- **401 Unauthorized**: the API key is invalid or expired; edit the account and save a new key.
- **Network errors**: check your connection/DNS, then retry.
- **Balance unavailable**: the provider may report the account as unavailable; retry later.
- **OpenRouter 401 / 402 / 429 / 5xx**: classified errors are shown on the account card; check the key, your OpenRouter payment state, request rate, or service status.
- **OpenRouter JSON/missing-field errors**: the official API shape may have changed; report the exact error text (without keys) as an issue.

## Filing an issue

- Search existing issues first.
- Include the app version, Windows version, and the exact error message.
- **Never include your real API key, Management Key, Credential Locker data, or unredacted logs.** Do not paste any key material into issues, including test keys.

Feature requests are welcome via the feature request template.
