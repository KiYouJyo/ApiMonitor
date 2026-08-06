日本語 | [简体中文](README.md) | [English](README.en.md)

# ApiMonitor

開発者向けのローカル優先 Windows API 残高・枠・サービス健全性モニタリングツール。

[![MIT License](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) [![CI](https://github.com/KiYouJyo/ApiMonitor/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/KiYouJyo/ApiMonitor/actions/workflows/ci.yml) [![GitHub Release](https://img.shields.io/github/v/release/KiYouJyo/ApiMonitor?display_name=tag&sort=semver)](https://github.com/KiYouJyo/ApiMonitor/releases/latest) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20WinUI%203-0078D4?logo=windows) ![x64](https://img.shields.io/badge/arch-x64-0078D4)

## アプリを入手

**GitHub サイドロード版（現在利用可能）**：現在のバージョンは **v1.0.0**（PackageVersion `1.0.0.2`）、x64、自己署名です。[最新の GitHub Release](https://github.com/KiYouJyo/ApiMonitor/releases/latest) から完全な `ApiMonitor_1.0.0.2_x64_Test.zip` をダウンロードし、解凍して `Install.cmd` を実行してください。更新確認は GitHub Releases を使用します。

**Microsoft Store 版**：PackageVersion は `1.0.0.0` で、Store 専用の ID（`JoKiy.ApiMonitor`）を使用します。Store パッケージは Partner Center にアップロード済みで、初回リリースの手続きを進めています。まだ公開されていないため、ダウンロードリンクは提供しません。

両チャネルの Package Family は異なり、**相互にその場でアップグレードすることはできません**。Store 版は新規インストールとして扱われ、GitHub サイドロード版のデータは**移行されません**。両チャネルを同時にインストールすることは推奨しません。

## ApiMonitor について

ApiMonitor は、複数の AI API プラットフォームと地図/GIS サービスの API 残高・枠・サービス健全性をローカルで監視する、開発者向け Windows ツールです。アカウント、残高、履歴、設定はすべてデバイスに保存され、テレメトリ・広告・開発者クラウド サーバーはありません。

## 主な機能

- 複数 Provider・複数アカウント管理
- API 残高、Credits、枠、サービス健全性の監視
- 自動更新（アプリ実行中のみ）
- 残高不足・サービス状態の通知（Windows 通知センター、既定オフ）
- ローカル履歴とデータ洞察（推移、消費見積もり、CSV エクスポート）
- 通知領域トレイ常駐、トレイに閉じる、ログイン時起動（既定オフ）
- フローティング残高ウィンドウ（常に手前に表示されるコンパクト表示）
- 初回起動ガイド
- アプリ健全性チェック（非機密のみの 21 項目診断）
- ローカル携帯バックアップ（鍵は一切含みません）
- 中国語（簡体字）、English、日本語
- ライト、ダーク、システム追従テーマ

## 対応サービス

**AI 残高・枠**

- DeepSeek
- OpenRouter（通常 API キーと Management Key）
- Moonshot / Kimi
- SiliconFlow
- xAI（Management API）

**地図・GIS サービス健全性**

- AMap
- Baidu Maps
- Tencent Location
- Tianditu
- SuperMap iServer
- OGC WMS / WMTS / WFS

地図プラットフォームは通常、インターフェースの健全性と資格情報の状態を提供するだけで、正確な残り枠を提供しない場合があります。アプリは偽の 0 やパーセントを表示しません。自己ホスト GIS のサービスアドレスはデバイスにのみ保存されます。

各 Provider の認証方式、指標定義、ホストホワイトリスト、セキュリティ制限は [docs/PROVIDERS.md](docs/PROVIDERS.md) と [docs/SECURITY-ARCHITECTURE.md](docs/SECURITY-ARCHITECTURE.md) を参照してください。

## インストール

GitHub サイドロード版は [Releases](https://github.com/KiYouJyo/ApiMonitor/releases/latest) から完全な `Test.zip` をダウンロードします。

1. `ApiMonitor_1.0.0.2_x64_Test.zip` をダウンロードして解凍します。
2. **`Install.cmd`** をダブルクリックし、UAC の確認を 1 回承認します。
3. 証明書の信頼、依存関係の確認、インストール/アップグレードが完了するまで待ちます。

詳細、SmartScreen の説明、終了コード、SHA-256 の確認方法は [packaging/installer/INSTALL.md](packaging/installer/INSTALL.md)、アンインストールは [UNINSTALL.md](packaging/installer/UNINSTALL.md) を参照してください。

## プライバシーとローカル設計

- API キーは Windows Credential Locker にのみ保存され、選択した Provider の公式エンドポイント（または明示的に設定した自己ホスト GIS アドレス）にのみ送信されます。
- アカウント、残高、履歴、設定はローカルのアプリデータディレクトリにのみ保存されます。テレメトリ、広告、開発者クラウド サーバー、クラウド同期はありません。
- 通知はローカルで生成され、アプリを終了すると停止します。
- GitHub 版は「更新を確認」をクリックしたときだけ GitHub Releases API にアクセスします。Store 版は StoreContext を使用し、GitHub のダウンロードページを開きません。
- 携帯バックアップと CSV エクスポートに鍵は含まれません。

[PRIVACY.md](PRIVACY.md) と [docs/SECURITY-ARCHITECTURE.md](docs/SECURITY-ARCHITECTURE.md) を参照してください。

## システム要件

- Windows 10 バージョン 1809（ビルド 17763）以降、x64
- Windows App Runtime 2.3.1 以降（Store 版は Store が依存関係を処理します）

## データとバックアップ

データファイルはローカルのアプリデータディレクトリに保存されます（アカウント、残高履歴、しきい値、設定は分離保存され、破損時は自動バックアップ）。設定ページで `.apimonitor-backup` のエクスポート/インポートが可能です（資格情報は含まず、安全なマージ方式）。詳細は [docs/DATA-STORAGE.md](docs/DATA-STORAGE.md) と [docs/BACKUP.md](docs/BACKUP.md) を参照してください。

## 言語

中国語（簡体字）、日本語、英語に対応しています。言語の切り替えにはアプリの再起動が必要です。

## ドキュメント

- [リリースガイド](docs/RELEASE.md)
- [Microsoft Store 公開ガイド](docs/STORE-PUBLISHING.md)
- [Provider 説明](docs/PROVIDERS.md)
- [データ保存](docs/DATA-STORAGE.md) · [バックアップ](docs/BACKUP.md) · [セキュリティアーキテクチャ](docs/SECURITY-ARCHITECTURE.md)
- [ロードマップ](docs/ROADMAP.md)
- [変更履歴](CHANGELOG.md)
- [サードパーティ声明](THIRD-PARTY-NOTICES.md)

## 開発とビルド

```powershell
dotnet restore ApiMonitor.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet test tests\ApiMonitor.Tests\ApiMonitor.Tests.csproj -c Debug
dotnet build ApiMonitor.slnx -c Debug -p:Platform=x64 --no-restore
```

配布チャネルはビルド時に `DistributionChannel` プロパティで決定します（`Development` / `GitHubSideload` / `MicrosoftStore`）。完全なビルド・チャネル分離・リリース手順は [docs/RELEASE.md](docs/RELEASE.md) を参照してください。

## フィードバック

問題は [GitHub Issues](https://github.com/KiYouJyo/ApiMonitor/issues) で報告してください。診断情報を送る前に、機密情報（実際の API キー、Authorization、Management Key、イントラネット GIS アドレス、アカウント情報を含む LocalState）が含まれていないか確認してください。セキュリティ問題は GitHub Private Vulnerability Reporting をご利用ください。

## ロードマップ

[docs/ROADMAP.md](docs/ROADMAP.md) を参照してください。ロードマップは方向性を示すものであり、バージョンや日付の約束ではありません。

## ライセンスとサードパーティ声明

本プロジェクトは [MIT License](LICENSE) で提供されます。依存コンポーネントの声明は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。本プロジェクトは DeepSeek、OpenRouter、Moonshot、SiliconFlow、xAI、各地図/GIS プラットフォーム、Microsoft とは一切の公式提携関係はありません。
