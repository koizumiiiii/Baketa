# Baketa プロジェクトドキュメント

Baketaは、ゲームプレイ中にリアルタイムでテキストを翻訳するオーバーレイアプリケーションです。このリポジトリには、Baketaプロジェクトの開発・設計に関する各種ドキュメントが含まれています。

## ドキュメント構成

### 静的ページ（GitHub Pages）
- [ランディングページ](pages/index.html) - 製品紹介ページ
- [メール確認完了ページ](pages/auth/confirm/index.html) - Supabase認証後のリダイレクト先

### 1. プロジェクト概要
- [プロジェクト概要](1-project/overview.md)
- [開発ロードマップ](1-project/roadmap.md)

### 2. 開発プロセス
- [開発環境セットアップガイド](2-development/environment-setup.md)
- [開発ワークフロー](2-development/workflow.md)

#### 開発ガイドライン
- [依存性注入ガイドライン](2-development/guidelines/dependency-injection.md)
- [名前空間構成ガイド](2-development/guidelines/namespace-migration.md)
- [新名前空間ガイド](2-development/guidelines/new-namespace-guide.md)
- [翻訳モデル名前空間移行ガイド](2-development/guidelines/translation-namespace-guide.md) *NEW*
- [名前空間統一状況アップデート](2-development/guidelines/namespace-migration-update.md) *NEW*
- [イベント集約機構の使用ガイド](2-development/guidelines/event-aggregator-usage.md)

#### コーディング規約
- [C#コーディング基本規約](2-development/coding-standards/csharp-standards.md)
- [モダンC#機能の活用](2-development/coding-standards/modern-csharp.md)
- [パフォーマンス最適化ガイドライン](2-development/coding-standards/performance.md)
- [プラットフォーム間相互運用](2-development/coding-standards/platform-interop.md)
- [.editorconfigガイドライン](2-development/coding-standards/editorconfig-guide.md)
- [テスト標準とベストプラクティス](2-development/coding-standards/testing-standards.md) *NEW*

#### 言語機能
- [C# 12サポートガイド](2-development/language-features/csharp-12-support.md)
- [C# 12実装ノート](2-development/language-features/csharp-12-implementation-notes.md)

### 3. アーキテクチャ

#### Clean Architecture設計
- [Clean Architecture概要](3-architecture/clean-architecture.md) *NEW* - 5層構造、依存関係、Phase 0分析結果

#### 戦略文書・改善計画
- [PaddleOCR安定性改善戦略](architecture/paddle-ocr-stability-improvement-strategy.md) - 恒久的解決アプローチによるOCR処理の安定性向上

#### プラットフォーム抽象化
- [プラットフォーム抽象化レイヤー](3-architecture/platform/platform-abstraction.md)

#### アダプターレイヤー
- [アダプター実装ベストプラクティス](3-architecture/adapters/adapter-implementation-best-practices.md)
- [アダプター実装サマリー](3-architecture/adapters/adapter-implementation-summary.md)
- [アダプターファクトリーパターン](3-architecture/adapters/adapter-implementation.md#9-アダプターファクトリーパターン)

#### コア抽象化
- [イメージ抽象化レイヤー](3-architecture/core/image-abstraction.md)
- [イメージ実装ガイド](3-architecture/core/image-implementation.md)

#### イベント集約機構
- [イベントシステム概要](3-architecture/event-system/event-system-overview.md)
- [イベント実装ガイド](3-architecture/event-system/event-implementation-guide.md)

#### UI システム
- [Avalonia UI実装計画](3-architecture/ui-system/avalonia-migration.md)
- [Avalonia UIガイドライン](3-architecture/ui-system/avalonia-guidelines.md)
- [ReactiveUI実装ガイド](3-architecture/ui-system/reactiveui-guide.md)
- [ReactiveUIバージョン互換性ガイド](3-architecture/ui-system/reactiveui-version-compatibility.md)
- [Issue56実装ノート](3-architecture/ui-system/issue56-implementation-notes.md)

#### OCR システム
- [PP-OCRv5統合設計](3-architecture/ocr-system/pp-ocrv5-integration.md) *NEW* - PP-OCRv5検出・認識モデル、ArrayPool最適化
- [OCRアプローチ](3-architecture/ocr-system/ocr-opencv-approach.md)
- [OCR設定UI設計](3-architecture/ocr-system/ocr-settings-ui.md)
- [OCR実装ガイド](3-architecture/ocr-system/ocr-implementation.md)
- [OCR前処理システム](3-architecture/ocr-system/preprocessing/index.md)
- [画像処理フィルター設計と実装](3-architecture/ocr-system/image-filters.md)

#### 翻訳システム
- [gRPC翻訳システム設計](3-architecture/translation/grpc-system.md) *NEW* - NLLB-200、HTTP/2通信、Phase 5.2D完了
- [翻訳エンジンインターフェース](3-architecture/translation/translation-interfaces.md)
- [名前空間統一による改善](3-architecture/architecture-namespace-unification.md)

#### キャプチャシステム
- [Windows Graphics Capture API](3-architecture/capture-system/windows-graphics-capture.md) *NEW* - C++/WinRT native DLL、DirectX/OpenGL対応

#### 認証システム
- [Supabase Auth基盤構築](issues/issue-133-supabase-auth-setup.md) - OAuth設定、データベーススキーマ
- [ログイン/登録UI実装](issues/issue-167-login-ui.md) - Email/Password、OAuth（Google/Discord/Twitch）
- [トークン管理](issues/issue-168-token-management.md) - Windows Credential Manager統合
- [認証UI拡張](issues/issue-169-auth-ui-extensions.md) - パスワードリセット等

### 4. 実装ガイド

- [アダプター修正ガイド](2-development/implementation/adapter-fixes.md) - Issues #46, #47, #48対応

### 5. UI設計

- [エンジン選択UI実装](3-architecture/ui-system/engine-selection-ui.md) - LocalOnly/CloudOnly設定画面

### 6. 開発ノート

開発過程での問題解決や注意点を記録したドキュメント集です。

- [翻訳基盤実装ノート](development-notes/translation-implementation-notes.md) - 翻訳基盤実装時の名前空間問題やHttpClient依存関係の解決策
- [名前空間統一の問題と計画](development-notes/namespace-unification-issue.md) - 翻訳モデルの名前空間競合問題とその解決計画
- [名前空間統一タスク](development-notes/namespace-unification-tasks.md) - 名前空間統一プロジェクトのタスクリスト (完了済み)
- [名前空間統一完了報告](development-notes/namespace-unification-completion-report.md) *NEW* - 名前空間統一プロジェクトの完了報告と学習した教訓
- [翻訳システム実装確認完了レポート](development-notes/baketa-translation-status.md) **最新** - Phase 5完了・実ファイル検証済み・プロダクション品質達成

## 最新の更新情報

**2025年11月27日** - **Supabase Auth統合・認証UI実装** 🔐

### 📋 **認証システム実装完了**

Supabase認証基盤とログイン/登録UIを実装しました。

#### **✅ 完了項目**
- **Supabase Auth基盤**: プロジェクト構築、OAuth設定（Google/Discord/Twitch）
- **データベース**: profilesテーブル、RLSポリシー、自動プロファイル作成トリガー
- **ログイン/登録UI**: Email/Password認証、OAuth認証ボタン
- **OAuthコールバック**: ローカルHTTPサーバーによるデスクトップアプリ対応
- **GitHub Pages**: メール確認完了ページ（docs/pages/auth/confirm/）

#### **📁 ドキュメント構造変更**
- 静的ページを`docs/pages/`に移動（GitHub Pages用）
- 認証システムセクションを追加

---

**2025年11月17日** - **ドキュメント整理・Phase 5.3完了情報反映** 📚

### 📋 **ドキュメント再編成完了**

プロジェクト全体のドキュメントを整理し、最新の実装（Phase 5.2〜5.3）に合わせて更新しました。

#### **✅ 完了項目**
- **ディレクトリ再編成**: 4-testing/, 4-implementation/, 4-ui/ → 2-development/, 3-architecture/に統合
- **refactoring/削除**: 情報をclean-architecture.mdに統合
- **古い情報削除**: OPUS-MT、SentencePiece、2段階翻訳、240テストの記述を削除
- **最新情報反映**: gRPC/NLLB-200、PP-OCRv5、Windows Graphics Capture、ArrayPool、1,518テスト

#### **📄 新規作成ドキュメント**
- [Clean Architecture概要](3-architecture/clean-architecture.md) - 5層構造、依存関係分析
- [gRPC翻訳システム設計](3-architecture/translation/grpc-system.md) - NLLB-200、HTTP/2通信
- [Windows Graphics Capture API](3-architecture/capture-system/windows-graphics-capture.md) - C++/WinRT実装
- [PP-OCRv5統合設計](3-architecture/ocr-system/pp-ocrv5-integration.md) - PaddleOCR最新モデル
- [テスト標準](2-development/coding-standards/testing-standards.md) - 1,518テストケースのベストプラクティス

#### **📊 更新されたドキュメント**
- [プロジェクト概要](1-project/overview.md) - Phase 5.3最新状況
- [開発ロードマップ](1-project/roadmap.md) - マイルストーン更新

---

**2025年8月24日** - **PaddleOCR安定性改善戦略策定完了** 🎯

PP-OCRv5統一移行後のPaddlePredictor run failed問題に対する包括的な恒久的解決戦略を策定しました。

### 📋 **戦略策定の背景と成果**

V5統一移行により「検出されたテキストチャンク数: 0」問題は完全解決したものの、継続実行時の安定性課題が判明。Geminiフィードバックとデータ分析により、根本原因を特定し恒久的解決策を立案しました。

#### **✅ 完了項目**
- **根本原因特定**: アンマネージドメモリ管理とステートフル共有の問題
- **技術的解決策**: Immutable OCR Service Patternによるファクトリアーキテクチャ
- **段階的実装計画**: Phase 1-3の具体的ロードマップ策定
- **リスク管理**: 潜在リスクと緩和策の明確化
- **成功判定基準**: 定量・定性指標による効果測定方法

#### **🎯 戦略の特徴**
- **恒久的解決**: 場当たり的対処でなくアーキテクチャレベルの改善
- **段階的移行**: リスクを最小化した3フェーズ実装計画
- **実装現実性**: 工数1-2週間での実現可能性確保
- **品質向上**: メモリ管理徹底とリソース競合の原理的排除

#### **📄 成果物**
- [PaddleOCR安定性改善戦略](architecture/paddle-ocr-stability-improvement-strategy.md) - 23,000文字の包括的戦略文書

**次のステップ**: Phase 1実装開始（Mat管理改善 → ファクトリ導入 → 全面移行）

---

**2025年6月6日** - **OCRシステム: Phase 1 ライブラリ選定完了** 🎯

Issue #37「PaddleOCR統合基盤の構築」のPhase 1が完了しました。包括的な評価により、**Sdcb.PaddleOCR (2.7.0.3)** を最適なライブラリとして選定しました。

### 📋 **Phase 1: ライブラリ評価と選定完了**

4つのPaddleOCRライブラリを技術的適合性、安定性、アーキテクチャ整合性の観点から評価し、最適解を決定しました。

#### **✅ 評価完了項目**
- **PaddleOCR.Net/PaddleSharp評価**: 技術仕様・.NET 8対応・メンテナンス状況
- **PaddleOCRSharp評価**: 機能性・安定性・ライセンス互換性
- **その他ライブラリ調査**: PaddleOCR、PaddleOCR.Onnx
- **選定根拠文書化**: 94/100点の包括的評価レポート作成

#### **🥇 選定結果: Sdcb.PaddleOCR**
- **技術的優位性**: .NET 8完全対応、OpenCvSharp統合、マルチスレッド対応
- **アーキテクチャ整合性**: 依存性注入対応、適切な抽象化、リソース管理
- **開発・保守性**: 活発なメンテナンス、包括的ドキュメント、エンタープライズ実績
- **リスク最小化**: Apache 2.0ライセンス、安定性実証済み

#### **📄 作成文書**
- [PaddleOCRライブラリ評価レポート](.github/issues/phase1_reports/issue37_phase1_paddleocr_evaluation.md)
- [Phase 1完了報告](.github/issues/phase1_reports/issue37_phase1_completion.md)

#### **✅ Phase 2: 基盤構築完了**
- **NuGetパッケージ統合**: Sdcb.PaddleOCR (2.7.0.3) + 関連ライブラリ追加完了
- **モデル管理基盤**: IModelPathResolverインターフェース+DefaultModelPathResolver実装
- **初期化システム**: PaddleOcrInitializerクラス実装、DIモジュール統合
- **アーキテクチャ整合**: クリーンアーキテクチャ準拠、適切な抽象化層実装
- **品質保証**: 全警告解消完了（15件→ 0件）、プロダクション品質達成 *NEW*
- [Phase 2完了報告](.github/issues/phase1_reports/issue37_phase2_completion.md)
- [警告解消報告](.github/issues/phase1_reports/issue37_warning_resolution.md) *NEW*

**次のステップ**: Phase 3 - OCRエンジン初期化システム実装

---

**2025年6月5日** - **Phase 5: 翻訳システム基盤完成** 🎉

翻訳エンジン状態監視機能・UI統合システムが完全に実装され、プロダクション品質に到達しました。

### ✅ 主要な達成項目

- **TranslationEngineStatusService** (568行): リアルタイム状態監視システム実装完了
- **TranslationSettingsViewModel** (725行): 統合設定管理ViewModel実装完了
- **SettingsFileManager** (527行): 完全永続化システム実装完了
- **UI統合システム**: WindowNotificationManager統合、15個のUIコンポーネント完全実装
- **コード品質**: CA警告0件、C# 12最新構文採用、プロダクション品質達成

**注**: この時点では従来の翻訳システムを使用していましたが、後にgRPC/NLLB-200システム（Phase 5.2D）に置き換えられました。

---

**2025年5月18日** - 翻訳モデルの名前空間統一プロジェクトが完了しました。すべての翻訳関連のデータモデルが `Baketa.Core.Translation.Models` 名前空間に統一され、型参照の曖昧さが排除されました。詳細は[名前空間統一完了報告](development-notes/namespace-unification-completion-report.md)を参照してください。

## ライセンス

Copyright © 2025 Baketa Project