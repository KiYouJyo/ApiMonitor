# 製品説明

ApiMonitor は、あなた自身の API アカウントの残高・クレジット・認証情報・地図/GIS サービスの健全性をローカルで監視する Windows デスクトップ アプリです（WinUI 3 / .NET 10、x64）。

## 複数 Provider・複数アカウント

11 の Provider に対応：AI 残高 Provider（DeepSeek、OpenRouter、Moonshot / Kimi、SiliconFlow、xAI）と地図/GIS 健全性 Provider（高徳、百度地図、Tencent Location、天地図、SuperMap iServer、汎用 OGC WMS/WMTS/WFS）。各 Provider で複数アカウントを管理でき、残高・クレジット・キー枠・サービス健全性を分けて表示します。地図/GIS アカウントは資金残高の集計に含まれません。

## ローカル優先

アカウント、残高、履歴、設定はすべてこのデバイスに保存されます。API キーは Windows Credential Locker にのみ保存され、テレメトリ・広告・開発者クラウド サーバーはありません。通知はローカルで生成されます。

## 主な体験

自動更新、残高通知、通知領域トレイ、フローティング ウィンドウ、データ洞察（推移と消費見積もり）、携帯バックアップ（鍵は含みません）、3 言語 UI（簡体字中国語 / English / 日本語）、ライト / ダーク / システム追従テーマ。

## 配布チャネル

GitHub サイドロード版は Releases で配布され手動で更新確認できます。Microsoft Store 版は Store 経由で更新され、新規インストールとして扱われます（旧サイドロードのデータは移行されません）。両チャネルのプライバシー動作は同じです。