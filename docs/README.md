# Baketa ドキュメント

このディレクトリには、Baketaプロジェクトの設計・開発に関するドキュメントが含まれています。

## ドキュメント構成

### GitHub Pages（静的ページ）

| ページ | 説明 |
|--------|------|
| [ランディングページ](pages/index.html) | 製品紹介（日英対応） |
| [利用規約](pages/terms-of-service.html) | サービス利用条件 |
| [プライバシーポリシー](pages/privacy-policy.html) | 個人情報取り扱い方針 |

#### 認証ページ

| ページ | 説明 |
|--------|------|
| [認証コールバック](pages/auth/callback/index.html) | ユニバーサルルーター |
| [メール確認完了](pages/auth/confirm/index.html) | 登録確認 |
| [パスワードリセット](pages/auth/reset-password/index.html) | パスワード再設定 |
| [招待確認](pages/auth/invite/index.html) | ユーザー招待 |
| [Magic Link](pages/auth/magic-link/index.html) | パスワードレスログイン |
| [メール変更完了](pages/auth/email-changed/index.html) | メールアドレス変更確認 |

---

### 1. プロジェクト概要

- [プロジェクト概要](1-project/overview.md)
- [開発ロードマップ](1-project/roadmap.md)

---

### 2. 開発ガイド

#### セットアップ・ワークフロー

- [開発環境セットアップ](2-development/environment-setup.md)
- [開発ワークフロー](2-development/workflow.md)

#### 開発ガイドライン

| ドキュメント | 内容 |
|-------------|------|
| [依存性注入ガイドライン](2-development/guidelines/dependency-injection.md) | DIパターン・モジュール設計 |
| [イベント集約機構](2-development/guidelines/event-aggregator-usage.md) | IEventAggregator使用法 |

#### コーディング規約

| ドキュメント | 内容 |
|-------------|------|
| [C#コーディング規約](2-development/coding-standards/csharp-standards.md) | 基本ルール |
| [モダンC#機能](2-development/coding-standards/modern-csharp.md) | C# 12活用 |
| [パフォーマンス最適化](2-development/coding-standards/performance.md) | 最適化ガイド |
| [テスト標準](2-development/coding-standards/testing-standards.md) | テスト規約 |

---

### 3. アーキテクチャ

#### 全体設計

- [Clean Architecture概要](3-architecture/clean-architecture.md) - 5層構造と依存関係
- [外部サービス連携](3-architecture/external-services.md) - Supabase、Cloudflare Workers

#### システム別設計

| システム | ドキュメント |
|---------|-------------|
| **OCR** | [OCR実装ガイド](3-architecture/ocr-system/ocr-implementation.md) |
| **翻訳** | [gRPC翻訳システム](3-architecture/translation/grpc-system.md) |
| **ROI Manager** | [自動調整型テキスト検出最適化](3-architecture/roi-system/roi-manager.md) |
| **統合AIサーバー** | [BaketaUnifiedServer](3-architecture/unified-ai-server.md) |
| **キャプチャ** | [Windows Graphics Capture](3-architecture/capture-system/windows-graphics-capture.md) |
| **認証** | [認証システム](3-architecture/auth/authentication-system.md) |
| **自動更新** | [自動アップデート](3-architecture/auto-update-system.md) |

#### UIシステム

- [Avalonia UIガイドライン](3-architecture/ui-system/avalonia-guidelines.md)
- [ReactiveUI実装ガイド](3-architecture/ui-system/reactiveui-guide.md)

#### イベントシステム

- [イベントシステム概要](3-architecture/event-system/event-system-overview.md)
- [イベント実装ガイド](3-architecture/event-system/event-implementation-guide.md)

---

### リリース用

- [README.md](release/README.md) - リリースパッケージ同梱用（エンドユーザー向け）

---

## 関連リンク

- [CLAUDE.md](../CLAUDE.md) - 開発ガイド（Claude Code用）
- [GitHub Issues](https://github.com/koizumiiiii/Baketa/issues) - 課題管理
- [GitHub Releases](https://github.com/koizumiiiii/Baketa/releases) - リリース履歴

---

## ライセンス

Copyright (c) 2024-2026 Baketa Project
