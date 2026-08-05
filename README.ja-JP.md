# ApiMonitor

[English](README.md) · [简体中文](README.zh-CN.md)

![CI](https://github.com/KiYouJyo/ApiMonitor/actions/workflows/ci.yml/badge.svg)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**ApiMonitor** は WinUI 3 製の軽量 Windows デスクトップアプリで、自分自身の API アカウント残高を照会し、ローカルに記録します。**DeepSeek**、**OpenRouter**、**Moonshot / Kimi**、**SiliconFlow**、**xAI** の残高照会に対応し、複数アカウント管理と Windows 通知センターの残高不足アラート（任意）を備えています。

- 現在のバージョン: **v0.8.0**（DisplayVersion `0.8.0` / PackageVersion `0.8.0.0`）
- ランタイム: .NET 10 / Windows App SDK 2.x、x64
- 配布: MSIX サイドロード（自己署名開発者証明書）、Microsoft Store は v1.0 で計画
- ライセンス: [MIT](LICENSE)
- 言語: 简体中文 · English · 日本語（設定 → 外観と言語 で切替）

## アップグレード

- **v0.8.0** は v0.7.0 の上に**その場でアップグレード**します。アカウント、AccountId、Credential Locker の API キー、最新残高、履歴、しきい値、自動更新・通知・トレイ・フローティングウィンドウ・サインイン起動・外観（テーマと言語）の設定はすべて保持されます。非機密の Provider 設定（xAI Team ID など）も保持され、キーは Credential Locker にのみ残ります。v0.7.0 のデータファイルの schema バージョン変更は不要です。
- **v0.7.0** は v0.6.0 の上に**その場でアップグレード**します。アカウント、AccountId、Credential Locker の API キー、最新残高、履歴、しきい値、自動更新・通知・トレイ・フローティングウィンドウ・サインイン起動・外観（テーマと言語）の設定はすべて保持されます。旧 `compact-window-settings.json` は初回起動時に一度だけ冪等に `floating-window-settings.json` へ移行されます。インストーラーは通知やサインイン起動を自動的に有効化しません。
- v0.6.0 は v0.5.0 の上にその場でアップグレードします（過去の経緯）。
- v0.5.0 は v0.4.0 の上にその場でアップグレードします（過去の経緯）。
- v0.2.0 のサイドロードパッケージはその場ではアップグレードできません。旧パッケージをアンインストールしてからアカウントを再追加してください。

## 主な機能

- Provider ごとに複数アカウント（例: DeepSeek 複数、OpenRouter キー複数）
- Provider はレジストリから動的に取得（UI にハードコードしない）。v0.8.0 では DeepSeek・OpenRouter・Moonshot / Kimi・SiliconFlow・xAI の 5 つを提供
- OpenRouter の 2 つの資格情報モード（通常 API Key / Management Key）
- **Moonshot / Kimi**（v0.8.0）: 通常の API キーで `GET https://api.moonshot.cn/v1/users/me/balance` を照会し、利用可能残高（人民元。公式の `available_balance` = 現金 + バウチャー）、現金残高、バウチャー残高を表示。欠落フィールドは `null`（決して `0` にしない）。主指標は利用可能残高で、現金とバウチャーを再集計しません。
- **SiliconFlow**（v0.8.0）: 通常の API キーで `GET https://api.siliconflow.cn/v1/user/info` を照会し、残高フィールドのみを読み取ります（主指標 `totalBalance`、補助 `balance` / `chargeBalance` / 任意の `grantedBalance`）。ユーザープロフィールは無視し、完全なレスポンスをログに書きません。公式の構造が変わった場合は「応答構造が未対応」を返し、誤って 0 を表示しません。
- **xAI**（v0.8.0）: 推論 API ではなく **Management API** を使用 — `GET https://management-api.x.ai/v1/billing/teams/{team_id}/prepaid/balance` を **Management Key** と **Team ID** で照会します。通常のモデル API キーでは照会できません。公式の「Representation of USD Cents」台帳値をドキュメントに従って米ドルのプリペイド Credits に換算し、マイナス（超過利用）は保持して切り捨てや `Math.Abs` を行いません。
- Provider 能力メタデータ（v0.8.0）: 各 Provider が公式 Base URL、必須の非機密設定フィールド（xAI Team ID など）、主指標、通貨、複数通貨 / 残高内訳 / 資格情報検証の有無を宣言。公式 Provider ではカスタムエンドポイントは**不可**です。
- アカウント概要（合計/残高不足/照会失敗）、Provider・状態フィルター
- 単一または全アカウント更新（アカウント単位の同時実行ロックを再利用）
- アカウント別の履歴・しきい値・自動更新・通知設定
- 汎用 **BalanceMetric** モデル: 不明値は `null`（決して `0` にしない）、無制限割当ては誤って低残高アラートを発しない
- **Windows 通知センターの残高不足アラート**（初回、繰り返しクールダウン、回復通知、テスト通知など）
- 通知領域（トレイ）常駐、閉じるとトレイへ、単一インスタンス、サインイン起動（任意）
- **フローティング残高ウィンドウ**（v0.7.0）: 白黒テーマ・固定サイズ・単一サーフェスの角丸スクエア情報ブロックで、選択した 1 アカウントのアカウント名・Provider・主残高・単位・短い状態を表示します。常に手前に表示され、タスクバーには表示されず、閉じてもアプリは終了しません。アカウントカードの**「フローティングウィンドウに設定」**またはトレイメニュー（**開く/閉じる**）から開閉・切り替えでき、最後の位置・選択アカウントを復元します（旧コンパクトウィンドウの設定は自動移行）。Windows ネイティブのなめらかなドラッグに対応し、旧コンパクトウィンドウを置き換えます。
- ホーム上部のレイアウトを簡素化し、旧サブタイトルを削除して空白をなくしました。
- アプリアイコン一式（EXE/タイトルバー、タスクバー、スタートメニュー、トレイ、スプラッシュ、Store 用アセット）を新しい `ApiMonitor.ico` / `TrayIcon.ico` とパッケージロゴ資産に置き換えました。
- **データ分析**ページ（v0.6.0）: アカウント/指標/期間の選択、軽量ローカル傾向グラフ（WinUI ネイティブ、チャートフレームワークなし）、現在値・期間内変化・最小/最大値、折りたたみ可能な履歴テーブル、CSV エクスポート
- **消費量の推定**（v0.6.0）: 有効な区間の中央値から 1 日あたり消費量と推定利用可能日数を計算（ローカル履歴のみ）。「推定値」と明記し、データ不足・消費未観測・最近チャージ・非対応指標・現在値不明などの理由を明示
- **ポータブルバックアップ**（v0.6.0、v0.7.0 / v0.8.0 で更新）: 設定 → データ管理 から `.apimonitor-backup`（ZIP+JSON）をエクスポート/インポート。アカウントメタデータ（xAI Team ID などの非機密設定を含む）、残高履歴、しきい値、各種設定を含みます。v0.8.0 / v0.7.0 のバックアップは `floating-window-settings.json` を使用し、旧 `compact-window-settings.json` を含む v0.6.0 のバックアップも引き続きインポートできます。**API キー、Management キー、資格情報は一切含まれません**。インポートは安全なマージ: 既存アカウントはローカル資格情報を保持、新規アカウントはキー再入力が必要とマーク、履歴は安定 ID で重複排除、失敗時はロールバック
- **テーマ**（v0.6.0）: システムに従う / ライト / ダーク。メインウィンドウとフローティングウィンドウに即時反映され、永続化
- **統一されたアプリシェル**（v0.6.0）: タイトルバー、ナビゲーションペイン、ページ背景がライト/ダーク/ハイコントラストで一貫したテーマサーフェスを共有
- **3 言語 UI**（v0.6.0）: 简体中文 / English / 日本語。言語を切り替えると設定を保存し、再起動を促して `AppInstance.Restart` で再起動します（部分的な半翻訳は発生しません）
- **完全な「このアプリについて」ページ**（v0.6.0）: 製品情報（DisplayVersion と PackageVersion を分離）、動的な Provider 一覧、プライバシー・セキュリティ概要、プロジェクトリンク、オフラインのローカルドキュメント、手動更新チェック（GitHub REST、クリック時のみ・自動ダウンロード/インストールなし）、診断情報のコピー（非機密）、ローカルデータフォルダーを開く

## セキュリティとプライバシー

- API キーは **Windows Credential Locker**（ApiMonitor リソース）にのみ保存。JSON、ログ、診断情報には一切含まれません
- キーは対応する Provider の公式 HTTPS ホストにのみ送信されます（DeepSeek `api.deepseek.com`、OpenRouter `openrouter.ai`、Moonshot `api.moonshot.cn`、SiliconFlow `api.siliconflow.cn` / `api.siliconflow.com`、xAI Management API `management-api.x.ai`）。送信前に共有のホストホワイトリストで検証し、非 HTTPS や非ホワイトリスト宛先は拒否します。xAI Management Key は `management-api.x.ai` にのみ送信され、推論エンドポイントには送信されません。
- 残高照会は副作用のない公式 GET エンドポイントのみを呼び出します。ApiMonitor はモデル推論リクエストを送信しないため、残高照会で Token 消費や課金は発生しません。
- タイムアウト・429・5xx は限定的かつキャンセル可能な再試行を行います（401 / 403 / 404 と設定系エラーは再試行しません）。
- アカウントメタデータ、残高スナップショット、履歴、設定、通知状態はローカルのアプリデータのみに保存
- 通知はローカルで生成され、引数は非機密の識別子のみ
- **クラウドプッシュ、WNS リモートプッシュ、テレメトリ、開発者サーバーは一切ありません**
- ポータブルバックアップと CSV エクスポートに API キー・資格情報・Authorization ヘッダー・ログ・ローカルパスは含まれません
- 更新チェックは「更新を確認」をクリックしたときのみ実行。アカウント/残高/デバイスデータは送信せず、自動ダウンロード・インストールもしません
- サインイン起動はユーザーが有効化した場合のみ（既定オフ）で、サインイン時はトレイに常駐するだけです

## 動作環境

- Windows 10 バージョン 1809（ビルド 17763）以降 / Windows 11 推奨
- x64
- Windows App Runtime 2.3.1 以降

## インストール

Release アセットの完全な **Test.zip** を推奨します。解凍後、`Install.cmd` をダブルクリックするだけです（UAC 確認は 1 回）。詳細は [INSTALL.md](packaging/installer/INSTALL.md) と [SUPPORT.md](SUPPORT.md) を参照してください。

> GitHub サイドロード版は自己署名開発者証明書で署名されています。インストーラーが信頼手順を自動化しますが、Windows のセキュリティを迂回するものではありません（証明書は Local Machine > Trusted People にのみインポートされ、SHA-256 と完全な Thumbprint を検証してからインストールします）。

## ソースからのビルド

. NET 10 SDK と Windows SDK が必要です。

```powershell
dotnet restore ApiMonitor.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet test tests\ApiMonitor.Tests\ApiMonitor.Tests.csproj -c Debug --no-restore
dotnet build ApiMonitor.slnx -c Debug -p:Platform=x64 --no-restore
```

## ドキュメント

- [プライバシーポリシー](PRIVACY.md)
- [セキュリティポリシー](SECURITY.md)
- [サポートドキュメント](SUPPORT.md)
- [変更履歴](CHANGELOG.md)
- [サードパーティ製の声明](THIRD-PARTY-NOTICES.md)
