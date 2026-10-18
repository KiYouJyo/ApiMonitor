ApiMonitor v{{DISPLAY_VERSION}} | GitHub one-click installer
=============================================================

English
-------
Fully extract this ZIP, then double-click 1-Install-ApiMonitor.cmd.
The installer automatically downloads the official v{{DISPLAY_VERSION}}
offline installer from KiYouJyo/ApiMonitor on GitHub, verifies its
pinned SHA-256 checksum, checks the package identity and certificate,
and asks for Windows UAC permission when required. The bundled public
certificate is automatically imported ONLY into the local machine
TrustedPeople store, never Trusted Root. No manual .cer setup is needed.
A working Internet connection is required during installation.
Do not run files inside payload directly. Never ignore Windows security
warnings for an installer from an untrusted source.

2-Uninstall-ApiMonitor.cmd uninstalls locally without any downloads or Internet access.
You must explicitly type YES. Uninstalling may delete package-local data.
The Microsoft Store edition is a separate package and is not affected.

Chinese / 中文
--------------
完整解压 ZIP 后，双击 1-Install-ApiMonitor.cmd，按提示确认 Windows UAC。
安装器将自动从 GitHub 下载官方 {{DISPLAY_VERSION}} 离线包，校验固定
SHA-256 与签名证书，然后完成依赖、证书与 MSIX 安装。无需手动导入 CER。
首次安装需要联网，证书仅写入 LocalMachine\\TrustedPeople，不会添加到
“受信任的根证书颁发机构”，不会关闭系统签名检查。
卸载请运行 2-Uninstall-ApiMonitor.cmd，全程离线，不会下载任何内容；需输入 YES 确认，并注意卸载可能清除本地数据。
Microsoft Store 版本与 GitHub 版本相互独立。

Japanese / 日本語
-----------------
ZIP を完全に展開し、1-Install-ApiMonitor.cmd をダブルクリックしてください。
Windows UAC を承認すると、GitHub 上の公式 {{DISPLAY_VERSION}} オフライン
パッケージを自動で取得し、固定 SHA-256、署名、発行者を検証して導入します。
CER を手動でインポートする必要はありません。最初のインストールには
インターネット接続が必要です。証明書の信頼先は
LocalMachine\\TrustedPeople のみであり、信頼されたルートには追加しません。
アンインストールは 2-Uninstall-ApiMonitor.cmd を実行してください。ネット接続不要でダウンロードもしません。YES と入力して確認してください。

Package version: {{PACKAGE_VERSION}}
Release: https://github.com/KiYouJyo/ApiMonitor/releases/tag/v{{DISPLAY_VERSION}}
No private keys, passwords, PFX/P12, credentials or user data are bundled.
