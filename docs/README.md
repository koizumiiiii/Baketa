# Baketa プロジェクトドキュメント

Baketaは、ゲームプレイ中にリアルタイムでテキストを翻訳するオーバーレイアプリケーションです。このリポジトリには、Baketaプロジェクトの開発・設計に関する各種ドキュメントが含まれています。

## プロジェクト管理リソース

### GitHub Issue管理
- **保存場所**: `E:\dev\Baketa\docs\.github\issues`
- **形式**: 各issueはMarkdown形式（`issue_[番号].md`）で保存
- **更新方法**: `download_issues.ps1`スクリプトを使用して最新のオープンIssueを一括ダウンロード
- **目的**: オフライン参照、検索、およびドキュメンテーション連携のため

## ドキュメント構成

### 1. プロジェクト概要
- [プロジェクト概要](1-project/overview.md)
- [開発ロードマップ](1-project/roadmap.md)

### 2. 開発プロセス
- [開発環境セットアップガイド](2-development/environment-setup.md)
- [開発ワークフロー](2-development/workflow.md)

#### 開発ガイドライン
- [依存性注入ガイドライン](2-development/guidelines/dependency-injection.md)
- [名前空間構成ガイド](2-development/guidelines/namespace-migration.md)

#### コーディング規約
- [C#コーディング基本規約](2-development/coding-standards/csharp-standards.md)
- [モダンC#機能の活用](2-development/coding-standards/modern-csharp.md)
- [パフォーマンス最適化ガイドライン](2-development/coding-standards/performance.md)
- [プラットフォーム間相互運用](2-development/coding-standards/platform-interop.md)

#### 言語機能
- [C# 12サポートガイド](2-development/language-features/csharp-12-support.md)
- [C# 12実装ノート](2-development/language-features/csharp-12-implementation-notes.md)

### 3. アーキテクチャ

#### プラットフォーム抽象化
- [プラットフォーム抽象化レイヤー](3-architecture/platform/platform-abstraction.md)

#### アダプターレイヤー
- [アダプター実装ガイド](3-architecture/adapters/adapter-implementation.md)
- [アダプターファクトリーパターン](3-architecture/adapters/adapter-implementation.md#9-アダプターファクトリーパターン)

#### コア抽象化
- [イメージ抽象化レイヤー](3-architecture/core/image-abstraction.md)
- [イメージ実装ガイド](3-architecture/core/image-implementation.md)

#### UI システム
- [Avalonia UI実装計画](3-architecture/ui-system/avalonia-migration.md)
- [Avalonia UIガイドライン](3-architecture/ui-system/avalonia-guidelines.md)

#### OCR システム
- [OCRアプローチ](3-architecture/ocr-system/ocr-opencv-approach.md)
- [OCR設定UI設計](3-architecture/ocr-system/ocr-settings-ui.md)
- [OCR実装ガイド](3-architecture/ocr-system/ocr-implementation.md)
- [OCR前処理システム](3-architecture/ocr-system/preprocessing/index.md)

### 4. テスト戦略

#### OCRシステムテスト
- [OpenCVラッパーテスト戦略](4-testing/ocr/opencv-wrapper-tests.md)

## ライセンス

Copyright © 2025 Baketa Project