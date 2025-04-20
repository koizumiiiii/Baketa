# Baketa プロジェクト - 全体アーキテクチャ設計

*作成日: 2025年4月20日*

## 1. アーキテクチャ概要

Baketaプロジェクトはクリーンアーキテクチャの原則に基づいて設計されており、明確な責任分担と疎結合を実現するための階層構造を採用しています。全体としてクリーンアーキテクチャのスタイルと依存性の方向性に従いつつ、Windows専用アプリケーションとしての最適化も考慮しています。

### 1.1 レイヤードアーキテクチャ

Baketaプロジェクトは主に4つの層から構成されています：

1. **Core層** - ドメインモデルと基本抽象化
2. **Infrastructure層** - 外部依存と技術的実装
3. **Application層** - ビジネスロジックとユースケース
4. **UI層** - ユーザーインターフェースとプレゼンテーション

各層は内向きの依存性原則に従い、外側の層が内側の層に依存する形になっています。

## 2. アーキテクチャ図

```
┌───────────────────────────────────────────────────────────────────┐
│                                                                   │
│  ┌───────────────────────────────────────────────────────────┐    │
│  │                        UI Layer                           │    │
│  │                                                           │    │
│  │     ┌─────────────────┐       ┌──────────────────┐        │    │
│  │     │  Baketa.UI      │       │ Baketa.UI        │        │    │
│  │     │  .Avalonia      │       │ .Core            │        │    │
│  │     └─────────────────┘       └──────────────────┘        │    │
│  │                                                           │    │
│  └───────────────────────────────────────────────────────────┘    │
│                              ▲                                    │
│                              │                                    │
│  ┌───────────────────────────────────────────────────────────┐    │
│  │                    Application Layer                      │    │
│  │                                                           │    │
│  │                    Baketa.Application                     │    │
│  │   ┌────────────┐  ┌────────────┐  ┌──────────────┐        │    │
│  │   │  Services  │  │  Handlers  │  │  DI          │        │    │
│  │   └────────────┘  └────────────┘  └──────────────┘        │    │
│  │                                                           │    │
│  └───────────────────────────────────────────────────────────┘    │
│                              ▲                                    │
│                              │                                    │
│  ┌───────────────────────────────────────────────────────────┐    │
│  │                   Infrastructure Layer                    │    │
│  │                                                           │    │
│  │  ┌────────────────┐  ┌───────────────┐  ┌──────────────┐  │    │
│  │  │ Platform       │  │ OCR           │  │ Translation  │  │    │
│  │  │  ┌──────────┐  │  │ ┌───────────┐ │  │ ┌──────────┐ │  │    │
│  │  │  │ Windows  │  │  │ │ PaddleOCR │ │  │ │ ONNX     │ │  │    │
│  │  │  └──────────┘  │  │ └───────────┘ │  │ └──────────┘ │  │    │
│  │  └────────────────┘  └───────────────┘  └──────────────┘  │    │
│  │                                                           │    │
│  └───────────────────────────────────────────────────────────┘    │
│                              ▲                                    │
│                              │                                    │
│  ┌───────────────────────────────────────────────────────────┐    │
│  │                        Core Layer                         │    │
│  │                                                           │    │
│  │  ┌───────────────┐ ┌─────────────┐ ┌────────────────────┐ │    │
│  │  │ Abstractions  │ │ Models      │ │ Common             │ │    │
│  │  │ ┌───────────┐ │ │             │ │                    │ │    │
│  │  │ │ Imaging   │ │ │             │ │                    │ │    │
│  │  │ │ Capture   │ │ │             │ │                    │ │    │
│  │  │ │ Platform  │ │ │             │ │                    │ │    │
│  │  │ └───────────┘ │ │             │ │                    │ │    │
│  │  └───────────────┘ └─────────────┘ └────────────────────┘ │    │
│  │                                                           │    │
│  └───────────────────────────────────────────────────────────┘    │
│                                                                   │
└───────────────────────────────────────────────────────────────────┘
```

## 3. レイヤー詳細

### 3.1 Core層 (Baketa.Core)

最も内側のレイヤーで、エンタープライズビジネスルールを含みます。主にインターフェース、データモデル、共通ユーティリティで構成されます：

- **Baketa.Core.Abstractions**: プラットフォーム非依存の抽象化インターフェース
  - **Imaging**: 画像処理抽象化（`IImage`, `IImageProcessor`）
  - **Capture**: キャプチャ機能抽象化（`ICaptureService`）
  - **Translation**: 翻訳機能抽象化（`ITranslationService`）
  - **OCR**: OCR機能抽象化（`IOcrEngine`, `IOcrService`）
  - **Factories**: ファクトリーインターフェース（`IImageFactory`）
  - **Platform**: プラットフォーム抽象化インターフェース

- **Baketa.Core.Models**: アプリケーションのデータモデル
  - `CaptureRegion`, `TranslationResult`, `OcrResult`など

- **Baketa.Core.Events**: イベント定義と集約機構
  - `CaptureCompletedEvent`, `TranslationCompletedEvent`など

- **Baketa.Core.Common**: 共通ユーティリティ
  - ヘルパー関数、拡張メソッド、一般的なクラスなど

### 3.2 Infrastructure層 (Baketa.Infrastructure)

外部サービス、フレームワーク、ライブラリとの連携を担当します：

- **Baketa.Infrastructure.Platform**: プラットフォーム依存の実装
  - **Windows**: Windows固有の実装
    - `WindowsImage`, `WindowsCaptureService`など
  - **Adapters**: アダプターパターンによる変換
    - `WindowsImageAdapter`, `WindowsImageAdapterFactory`など

- **Baketa.Infrastructure.OCR**: OCR機能の実装
  - **PaddleOCR**: PaddleOCRベースのOCR実装
  - **Services**: OCRサービス実装
  - **Optimization**: OpenCVベースの最適化

- **Baketa.Infrastructure.Translation**: 翻訳機能の実装
  - **Engines**: 翻訳エンジン実装
  - **ONNX**: ONNXモデルを使用した翻訳
  - **Cloud**: クラウド翻訳サービス連携

- **Baketa.Infrastructure.Persistence**: データ永続化
  - **Settings**: 設定の保存と読み込み
  - **Cache**: キャッシュ機能

### 3.3 Application層 (Baketa.Application)

アプリケーションの中心的なビジネスロジックを担当します：

- **Baketa.Application.Services**: アプリケーションサービス
  - **OCR**: OCRアプリケーションサービス
  - **Translation**: 翻訳アプリケーションサービス
  - **Capture**: キャプチャアプリケーションサービス
  - **Integration**: 統合サービス

- **Baketa.Application.Handlers**: イベントハンドラー
  - 各種イベントの処理ロジック

- **Baketa.Application.DI**: 依存性注入の設定
  - モジュール化されたサービス登録

- **Baketa.Application.Configuration**: アプリケーション設定
  - 設定モデルと管理機能

### 3.4 UI層 (Baketa.UI)

ユーザーインターフェースとプレゼンテーションロジックを担当します：

- **Baketa.UI.Core**: UI共通機能
  - **Services**: UI共通サービス
  - **Controls**: 共通コントロール
  - **ViewModels**: 共通ビューモデル

- **Baketa.UI.Avalonia**: Avalonia UI実装
  - **Views**: XAML Views
  - **ViewModels**: ViewModels
  - **Controls**: カスタムコントロール
  - **Services**: Avalonia固有サービス

## 4. 主要なコンポーネント間の連携

### 4.1 画像処理フロー

1. **キャプチャ**
   - `ICaptureService`（Application層）がキャプチャを実行
   - `WindowsCaptureService`（Infrastructure層）がWindows APIを使用して画面をキャプチャ
   - `WindowsImage`としてキャプチャ結果を返す
   - `WindowsImageAdapter`を通じて`IImage`に変換

2. **画像処理**
   - `IImageProcessor`（Core層）が画像処理操作を定義
   - `OpenCvImageProcessor`（Infrastructure層）がOpenCVを使用して実装
   - 処理された画像は`IImage`として返される

3. **OCR処理**
   - `IOcrService`（Application層）がOCR処理を実行
   - `PaddleOcrEngine`（Infrastructure層）がOCR認識を担当
   - 認識結果は`OcrResult`（Core層のモデル）として返される

4. **翻訳処理**
   - `ITranslationService`（Application層）が翻訳処理を実行
   - `OnnxTranslationEngine`または`CloudTranslationService`が実際の翻訳を担当
   - 翻訳結果は`TranslationResult`として返される

### 4.2 イベント集約機構

各コンポーネント間の通信は`IEventAggregator`を通じて疎結合に行われます：

1. キャプチャが完了すると`CaptureCompletedEvent`を発行
2. OCRサービスがイベントを購読し、OCR処理を実行
3. OCR処理が完了すると`OcrCompletedEvent`を発行
4. 翻訳サービスがイベントを購読し、翻訳処理を実行
5. 翻訳が完了すると`TranslationCompletedEvent`を発行
6. UI層がイベントを購読し、結果を表示

## 5. 横断的関心事（クロスカッティングコンサーン）

### 5.1 ロギング

すべての層で一貫したロギングを実現するために、`ILogger`インターフェースを使用します。ロガーは依存性注入を通じて各コンポーネントに提供されます。

### 5.2 例外処理

各層で適切に例外をキャッチし、必要に応じてラップして上位層に伝えます。UI層では最終的なエラーハンドリングを行い、ユーザーフレンドリーなエラーメッセージを表示します。

### 5.3 パフォーマンス最適化

- **差分検出**: 画面変更の効率的な検出
- **並列処理**: 重い処理の並列実行
- **キャッシュ**: 翻訳結果などのキャッシュ

## 6. 拡張ポイント

アーキテクチャには以下の拡張ポイントがあります：

1. **新しい翻訳エンジン**: `ITranslationEngine`インターフェースを実装
2. **新しいOCRエンジン**: `IOcrEngine`インターフェースを実装
3. **画像処理フィルター**: `IImageFilter`インターフェースを実装
4. **新しいUI実装**: UIレイヤーを置き換え可能

## 7. 依存性注入

アプリケーション全体でMicrosoft.Extensions.DependencyInjectionを使用して依存性注入を実現します。各レイヤーは自身のサービス登録を提供します：

```csharp
// Program.cs
var services = new ServiceCollection();

// 各レイヤーのサービスを登録
services.AddBaketaCoreServices();
services.AddBaketaInfrastructureServices();
services.AddBaketaApplicationServices();
services.AddBaketaUIServices();

var serviceProvider = services.BuildServiceProvider();
```

## 8. まとめ

Baketaプロジェクトのアーキテクチャは、クリーンアーキテクチャの原則に基づいた4層構造を採用しています。明確な責任分担と疎結合な設計により、保守性と拡張性の高いアプリケーションを実現します。Windows専用アプリケーションではありますが、コアビジネスロジックはプラットフォームに依存しない形で実装されており、将来的な拡張の可能性も残しています。