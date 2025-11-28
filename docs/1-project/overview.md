# Baketaプロジェクト概要

*最終更新: 2025年11月28日*

> **プロダクション状態**: Baketa翻訳システムは**Phase 5.3完全実装達成**および**認証システム完全実装（Issue #167, #168）**により、プロダクション環境での安定運用が可能な状態です。gRPC翻訳システム、PP-OCRv5統合、Windows Graphics Capture API、ArrayPoolメモリ最適化、Supabase Auth統合が完了し、1,500+テストケース（100%成功率）で品質が実証されています。

## 1. プロジェクト概要

Baketaは、ゲームプレイ中にリアルタイムでテキストを翻訳するWindows専用オーバーレイアプリケーションです。ゲーム画面上のテキストをOCR技術で検出し、翻訳結果を透過オーバーレイとして表示します。

> **注意**: Baketaプロジェクトは現在、Windows専用アプリケーションとして開発されています。内部設計ではプラットフォーム抽象化レイヤーを採用していますが、現時点ではWindows対応のみを実装し、他プラットフォームへの対応予定はありません。OCR最適化にはOpenCVを使用します。

### 1.1 主要機能

#### **🔍 OCR・画像処理システム**
- **PP-OCRv5統合**: PaddleOCR PP-OCRv5検出・認識モデル（Phase 37完了）
- **Windows Graphics Capture API**: C++/WinRT native DLL（DirectX/OpenGL対応）
- **OpenCVベース最適化**: 画像前処理とArrayPoolメモリ管理（86%リーク削減）
- **差分検出**: 画面変更の高精度検出によるパフォーマンス最適化

#### **🌐 多言語翻訳システム（プロダクション運用中）**
- **gRPC翻訳システム**: HTTP/2通信によるC# ↔ Python連携（Phase 5.2D完了）
  - NLLB-200モデル（facebook/nllb-200-distilled-600M、200+言語対応）
  - Keep-Alive対応（10秒間隔、112秒アイドルタイムアウト防止）
  - 自動サーバー起動・ヘルスチェック・Ready状態監視
  - CTranslate2最適化（80%メモリ削減、2.4GB→500MB）
- **ハイブリッド翻訳**: ローカル（NLLB-200 gRPC）とクラウド（Gemini API）のインテリジェントフォールバック
- **多言語対応**: 200+言語サポート、東アジア言語（ja/en/zh/ko）含む

#### **🔍 翻訳エンジン状態監視機能（新実装）**
- **リアルタイム状態監視**: LocalOnly/CloudOnly/Networkの3系統完全監視
- **フォールバック記録機能**: レート制限・ネットワークエラー時の自動切り替え記録
- **ヘルスチェック機能**: モデルファイル・メモリ・ API接続の包括的確認

#### **🎨 UIシステム（完全実装済み）**
- **オーバーレイ表示**: 透過的なUI、クリックスルー対応
- **Shotボタン**: ワンクリック単発翻訳機能（トグル動作でオーバーレイ表示/非表示切り替え）
- **統合設定管理**: エクスポート・インポート・自動保存・妥当性検証完全実装
- **通知システム**: WindowNotificationManager統合・確認ダイアログ実装
- **ゲームプロファイル**: ゲーム別の最適設定

#### **🔐 認証システム（完全実装済み - Issue #167, #168）**
- **Supabase Auth統合**: Email/Password認証、OAuth認証（Google/Discord/Twitch）
- **ログイン/登録UI**: ReactiveUIベースのログイン・サインアップ画面
- **OAuthコールバック**: ローカルHTTPサーバーによるデスクトップアプリ対応（PKCEフロー）
- **トークン管理**: Windows Credential Manager統合、TokenExpirationHandler
- **パスワード強度検証**: 3種類以上文字種要件、ブラックリストチェック、強度インジケーター
- **GitHub Pages**: ランディングページ、利用規約、プライバシーポリシー、パスワードリセット

### 1.2 差別化要素（プロダクション達成済み）

#### **🏆 翻訳システムの先進性**
- **gRPC高性能通信**: HTTP/2による低レイテンシC# ↔ Python連携
- **NLLB-200統合**: Meta社の最先端多言語モデル（200+言語、2.4GB）
- **CTranslate2最適化**: 80%メモリ削減（2.4GB→500MB）、高速推論
- **ハイブリッド翻訳エンジン**: ローカル（高速・無料）とクラウド（高品質・有料）のインテリジェントフォールバック

#### **🔍 高度なシステム監視機能**
- **リアルタイム状態監視**: 翻訳エンジン状態、ネットワーク接続、フォールバック状況の3系統監視
- **インテリジェントフォールバック**: レート制限・ネットワークエラー時の自動切り替えと詳細ログ記録
- **包括的ヘルスチェック**: モデルファイル存在確認、メモリ使用量監視、API接続状態確認

#### **🎨 ユーザーエクスペリエンスの卓越性**
- **高度な設定管理**: エクスポート・インポート・自動保存・妥当性検証の完全自動化
- **直感的通知システム**: WindowNotificationManager統合、確認ダイアログ、適切なユーザーフィードバック
- **高カスタマイズ性**: オーバーレイの表示位置や見た目を詳細に調整可能

#### **⚡ パフォーマンスと品質**
- **Windows Graphics Capture API**: C++/WinRT native DLL、DirectX/OpenGL完全対応
- **PP-OCRv5統合**: 最新PaddleOCR検出・認識モデル（Phase 37完了）
- **ArrayPoolメモリ最適化**: 86%メモリリーク削減（Phase 5.2C）
- **高度な差分検出**: 画面変更を高精度で検出し、不要な処理をスキップ
- **低リソース消費**: ゲームパフォーマンスへの影響を最小限に抑えた設計
- **プロダクション品質**: 1,518テストケース100%成功率（Phase 5.3）

## 2. アーキテクチャ

Baketaプロジェクトは5つの主要レイヤーから構成されるクリーンアーキテクチャを採用します：

```
Baketa/
├── Baketa.Core/               # コア機能と抽象化
│   ├── Common/                # 共通ユーティリティ
│   ├── Abstractions/          # インターフェース
│   │   ├── Imaging/           # 画像抽象化インターフェース
│   │   └── Platform/          # プラットフォーム抽象化インターフェース
│   ├── Models/                # モデルクラス
│   └── Translation/           # 翻訳関連機能
│       └── Models/            # 翻訳モデル
│
├── Baketa.Infrastructure/     # インフラストラクチャ層
│   ├── OCR/                   # OCR機能実装
│   ├── Translation/           # 翻訳機能実装
│   │   ├── Clients/           # gRPCクライアント（NLLB-200 Python連携）
│   │   ├── Cloud/             # クラウド翻訳（Gemini API）
│   │   ├── Adapters/          # 翻訳エンジンアダプター
│   │   ├── Services/          # PythonServerManager等
│   │   └── Protos/            # gRPC Protocol Buffers定義
│   └── Services/              # その他サービス実装
│
├── Baketa.Infrastructure.Platform/  # プラットフォーム依存機能
│   ├── Abstractions/          # プラットフォーム抽象化インターフェース
│   ├── Windows/               # Windows実装
│   │   └── NativeMethods/     # P/Invoke定義
│   └── Adapters/              # アダプターレイヤー
│
├── Baketa.Application/        # アプリケーション層 
│   ├── Services/              # アプリケーションサービス
│   ├── DI/                    # 依存性注入管理
│   └── Events/                # イベント処理
│
├── Baketa.UI/                 # UI層
│   ├── Avalonia/              # Avalonia UI実装
│   │   ├── ViewModels/        # MVVMビューモデル
│   │   ├── Views/             # XAMLビュー
│   │   ├── Controls/          # カスタムコントロール
│   │   └── Services/          # UIサービス
│   └── Abstractions/          # UI抽象化
```

### 2.1 Baketa.Core

プラットフォーム非依存のコア機能と抽象化を提供します：

- 基本的なインターフェースと抽象化
- データモデル
- イベント集約機構
- 共通ユーティリティ
- 翻訳関連モデル（Baketa.Core.Translation.Models）

### 2.2 Baketa.Infrastructure

プラットフォーム非依存のインフラストラクチャサービスを提供します：

- **OCRエンジン**: PP-OCRv5（PaddleOCR）とOpenCV最適化
- **翻訳システム**:
  - **gRPC翻訳クライアント**: HTTP/2によるC# ↔ Python連携（NLLB-200）
  - **クラウド翻訳**: Google Gemini API 統合
  - **PythonServerManager**: 自動サーバー起動・ヘルスチェック・Keep-Alive
  - **ハイブリッドフォールバック**: ローカル（NLLB-200 gRPC）⇄クラウド（Gemini）自動切り替え
- **永続化機能**: 翻訳キャッシュ、設定管理
- **共通サービス実装**: レート制限、メトリクス管理

### 2.3 Baketa.Infrastructure.Platform

Windows固有の実装を担当します：

- プラットフォーム抽象化インターフェース
- Windows固有のプラットフォームサービス
- 画面キャプチャ実装
- 画像処理最適化（OpenCV）
- システムトレイとホットキー
- プラットフォーム依存機能のアダプター
- P/Invoke定義（Windows API呼び出し）

### 2.4 Baketa.Application

ビジネスロジックと機能統合を担当します：

- アプリケーションサービス
- 依存性注入管理
- イベントハンドラー
- 設定管理

### 2.5 Baketa.UI

ユーザーインターフェースとプレゼンテーションロジックを担当します：

- Avalonia UI実装
- ビューモデル
- UI固有サービス
- カスタムコントロール

## 3. 技術スタック

### 3.1 基本構成

- **フレームワーク**: .NET 8 (LTS)
- **UI技術**: Avalonia UI（ReactiveUI）
- **アーキテクチャ**: MVVMパターン、Clean Architecture 5層構造
- **OCRエンジン**: PaddleOCR PP-OCRv5 + OpenCV最適化
- **翻訳システム**:
  - **ローカル**: NLLB-200（facebook/nllb-200-distilled-600M、gRPC経由）
  - **クラウド**: Google Gemini API
  - **通信**: gRPC（HTTP/2）、Keep-Alive対応
  - **最適化**: CTranslate2（80%メモリ削減）
- **ネイティブDLL**: BaketaCaptureNative.dll（C++/WinRT、Windows Graphics Capture API）

### 3.2 gRPC翻訳システム（Phase 5.2D完了）

#### **🚀 高性能通信アーキテクチャ**
- **プロトコル**: gRPC（HTTP/2）
- **C# Client**: `GrpcTranslationClient`（Grpc.Net.Client）
  - Keep-Alive: 10秒間隔（112秒アイドルタイムアウト防止）
  - 自動再接続: `WithWaitForReady(true)`
  - タイムアウト: 30秒/リクエスト
- **Python Server**: `grpc_server/start_server.py`（port 50051）
  - NLLB-200エンジン: facebook/nllb-200-distilled-600M（2.4GB）
  - CTranslate2最適化: 80%メモリ削減（2.4GB→500MB）
  - 自動起動: PythonServerManager統合

#### **📊 翻訳機能**
- **対応言語**: 200+言語（NLLB-200）
- **バッチ翻訳**: 最大32テキスト同時処理
- **RPC Methods**:
  - `Translate()`: 単一テキスト翻訳
  - `TranslateBatch()`: バッチ翻訳
  - `HealthCheck()`: サーバー状態確認
  - `IsReady()`: モデル準備状態確認

#### **🔍 インテリジェントフォールバック**
- **リアルタイム状態監視**: gRPCサーバー状態、ネットワーク接続監視
- **自動フォールバック**:
  - gRPCエラー時: Gemini API自動切り替え
  - ネットワークエラー時: オフラインモード遷移
  - レート制限時: 詳細ログ記録
- **ヘルスチェック**: 5秒間隔の自動監視

### 3.3 Windows固有技術

- **プラットフォーム抽象化**: 内部的に抽象化レイヤーを使用するが、現在はWindows実装のみ
- **Windows実装**:
  - 画面キャプチャ: Windows GDI, Direct3D
  - オーバーレイ: Windows層化ウィンドウ（WS_EX_LAYERED）
  - システムトレイ: Windows通知領域API
  - ホットキー: Windows RegisterHotKey API

## 4. イメージ処理アプローチ

Baketaは特にOCR機能を強化するために、下記の画像処理アプローチを採用します：

### 4.1 画像前処理パイプライン

1. **キャプチャ**: 画面または領域のキャプチャ
2. **グレースケール変換**: カラー→グレースケール変換
3. **ノイズ除去**: ガウシアンフィルタなどによるノイズ除去
4. **コントラスト強調**: ヒストグラム均一化
5. **二値化**: 適応的閾値処理
6. **モルフォロジー演算**: 膨張・収縮による文字形状の整形
7. **テキスト領域検出**: MSERまたはエッジベースの検出
8. **ゲーム特性に基づく調整**: ゲームプロファイル設定の適用

### 4.2 差分検出の最適化

ゲームのパフォーマンスを低下させないよう、差分検出を最適化：

- サンプリングベースの高速検出
- テキスト領域に焦点を当てた変化検出
- ヒストグラム分析による効率的な差分識別

### 4.3 プロファイルベース最適化

プロファイルに基づく画像処理パラメータの最適化：

- ゲームごとの専用プロファイル
- 自動パラメータチューニング
- 結果フィードバックによる継続的改善

## 5. プラットフォーム抽象化レイヤーの役割

Baketaプロジェクトは、内部設計として抽象化レイヤーを採用していますが、現時点ではWindows専用アプリケーションとして実装されています：

### 5.1 設計原則

1. **コードの整理**: 関心事の分離によるコード整理とメンテナンス性向上
2. **テスト容易性**: モック可能なインターフェースを通じてテスト容易性を向上
3. **明示的な依存性**: ネイティブAPI呼び出しを明確に分離
4. **コード品質向上**: 明確な責任分離によるコード品質の向上

### 5.2 主要インターフェース

以下のWindows機能へのアクセスを抽象化インターフェースを通して提供:

- **IWindowManager**: Windows ウィンドウ管理機能
- **IScreenCapturer**: 画面キャプチャ機能
- **IKeyboardHook**: キーボードフック機能
- **IImage**: Windows画像表現の抽象化

## 6. 開発ガイドライン

### 6.1 命名規則

- **名前空間**: Baketa.{レイヤー}.{機能領域}
- **インターフェース**: I{名前}
- **抽象クラス**: {名前}Base
- **実装クラス**: {機能}{タイプ}（例: OpenCvImageProcessor）
- **ファイル名**: クラス名と一致させる

### 6.2 コード編成

- 関心の分離を厳格に守る
- 一つのクラスは一つの責任のみを持つ
- サービス間の依存は常にインターフェースを通じて行う
- ビジネスロジックとプラットフォーム固有コードを分離する

### 6.3 ドキュメント

- パブリックAPIには常にXMLドキュメントコメントを付ける
- アーキテクチャの決定は文書化する
- コンポーネント間の連携は図表を含めて説明する

## 7. 実装計画

1. **基盤実装**
   - クリーンアーキテクチャの基盤を構築
   - 名前空間構造の整理
   - 基本インターフェースの定義

2. **コア機能の実装**
   - イメージ抽象化レイヤーの実装
   - OCRシステムの連携
   - 翻訳システムの統合

3. **UI実装**
   - Avalonia UIフレームワークの実装
   - メインウィンドウとオーバーレイ実装
   - 設定UIの実装

4. **配布と運用**
   - インストーラー作成
   - 更新システム構築
   - エラー報告・診断システム

## 8. 最近の改善点（Phase 5.2〜Phase 5.3完了）

### 8.1 ArrayPoolメモリ最適化（Phase 5.2C - 2025年11月完了）

画像処理パイプラインでのメモリリーク問題を根本的に解決：

**問題**：
- 翻訳1回あたり平均577MBのメモリリーク
- `byte[]`の大量コピーによるGC圧迫

**解決策**：
- `ArrayPool<byte>.Shared`を全面採用
- `try-finally`ブロックでバッファ確実返却
- `MemoryStream`ラッパーでアロケーション削減

**成果**：
- **86%メモリリーク削減**（577MB→138MB）
- 起動時メモリ使用量90%削減
- GC頻度の大幅低減

### 8.2 gRPC翻訳システム完成（Phase 5.2D - 2025年11月完了）

NLLB-200 Pythonサーバーとの高性能gRPC通信を実現：

**主な実装成果**：
- **HTTP/2 gRPC通信**: C# ↔ Python低レイテンシ連携
- **Keep-Alive対応**: 10秒間隔、112秒アイドルタイムアウト防止
- **初回接続問題解決**: `WithWaitForReady(true)`でUNAVAILABLEエラー解消
- **自動サーバー起動**: PythonServerManager統合
- **CTranslate2最適化**: 80%メモリ削減（2.4GB→500MB）

**技術詳細**：
- Protocol: gRPC（port 50051）
- Model: facebook/nllb-200-distilled-600M（200+言語）
- RPC Methods: Translate, TranslateBatch, HealthCheck, IsReady

### 8.3 PP-OCRv5統合完了（Phase 37 - 2025年11月完了）

最新PaddleOCR PP-OCRv5検出・認識モデルを統合：

**主な実装成果**：
- **PP-OCRv5 Detection Model**: 最新テキスト検出エンジン
- **PP-OCRv5 Recognition Model**: 高精度文字認識エンジン
- **PaddleOcrEngine.cs**: 5,741行の統合実装

**効果**：
- OCR精度の向上
- 多言語テキスト検出の改善
- ゲーム画面特化最適化

### 8.4 モデルプリウォーミング最適化（Phase 5.2E - 2025年11月完了）

起動時のモデル初期化フローを最適化：

**問題**：
- ユーザーがモデル読み込み完了前に翻訳開始可能
- 初回翻訳が遅延

**解決策**：
- `IHostedService`による起動時バックグラウンド初期化
- Startボタン無効化→初期化完了後に有効化
- UI応答性維持

**効果**：
- 初回翻訳が常に高速に開始
- ユーザー体験の向上

### 8.5 包括的テストカバレッジ達成（Phase 5.3 - 2025年11月完了）

プロダクション品質を実証する包括的テストスイート完成：

**テスト規模**：
- **合計**: 1,518テストケース
- **成功率**: 100%（失敗0件、スキップ0件）
- **実行時間**: 約1.4分

**プロジェクト別内訳**：
- Baketa.Core.Tests: 511件
- Baketa.Infrastructure.Tests: 492件
- Baketa.Application.Tests: 415件
- Baketa.UI.Tests: 74件
- Baketa.UI.IntegrationTests: 20件
- Baketa.Integration.Tests: 6件

**品質保証**：
- すべての主要機能を網羅
- gRPC、ArrayPool、PP-OCRv5の安定性実証
- CI/CD自動実行対応

### 8.6 翻訳エンジン状態監視・UI統合（Phase 5 - 2025年6月完了）

プロダクションレベルの状態監視とUI統合システムを実装：

**状態監視機能**：
- `TranslationEngineStatusService`: リアルタイム状態監視
- 3系統完全監視（LocalOnly/CloudOnly/Network）
- Observableパターンによるイベント駆動
- フォールバック記録・詳細ログ

**UI統合システム**：
- `TranslationSettingsViewModel`: 統合設定管理（725行）
- `SettingsFileManager`: 永続化システム（527行）
- 通知システム: WindowNotificationManager統合
- 設定管理: エクスポート・インポート・自動保存

**コード品質**：
- C# 12最新構文採用
- Clean Architecture準拠
- 1,518テストケース100%成功率（Phase 5.3更新）