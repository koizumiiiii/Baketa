# Phase 0.2: Baketa全体フロー調査レポート

**調査日**: 2025-10-04
**調査目的**: 次の大規模リファクタリング（Phase 2以降）に向けた全体像の深い理解
**調査方法**: UltraThink方法論による段階的深堀り調査

---

## エグゼクティブサマリー

Baketaアプリケーションの4つの主要フロー（キャプチャ → OCR → 翻訳 → オーバーレイ表示）を完全調査し、アーキテクチャ、データフロー、技術的負債を特定しました。

### 主要な発見事項

1. **キャプチャフロー**: 高度に構造化された適応的キャプチャシステム（4つの戦略 + フォールバック）
2. **OCRフロー**: 巨大ファイル問題（PaddleOcrEngine.cs: 5,695行、47メソッド）
3. **翻訳フロー**: 複雑な座標ベース翻訳システム（Phase 12実装）
4. **オーバーレイフロー**: 統一システム（PHASE18）+ レガシーシステムの二重実装

### 緊急対応が必要な技術的負債

| 優先度 | 問題 | 影響範囲 | 推奨対応 |
|--------|------|----------|----------|
| **P0** | PaddleOcrEngine.cs 5,695行 | 保守性、テスト容易性 | 責務分離（7-10クラスに分割） |
| **P1** | OptimizedPythonTranslationEngine 2,765行 | gRPC移行後に削除予定 | Phase 3完了後の削除計画 |
| **P1** | WIDTH_FIX問題 | オーバーレイ表示 | 根本原因調査と恒久的解決 |
| **P2** | SharpDX依存（WinRTWindowCapture） | .NET 8互換性 | 廃止計画策定 |

---

## 1. キャプチャフロー調査

### 1.1 フロー概要

```
User Action: Startボタンクリック
  ↓
MainWindowViewModel.ExecuteStartCaptureAsync() (Line 551)
  ↓ PublishEventAsync(StartCaptureRequestedEvent)
  ↓
TranslationFlowEventProcessor.HandleAsync(StartCaptureRequestedEvent) (Line 250)
  ↓ アクティブウィンドウ取得 → StartTranslationRequestEventに変換
  ↓
TranslationFlowEventProcessor.HandleAsync(StartTranslationRequestEvent) (Line 88)
  ↓ ProcessTranslationAsync()呼び出し
  ↓
TranslationOrchestrationService.StartAutomaticTranslationAsync() (Line 181)
  ↓ バックグラウンドTask.Runで自動翻訳ループ開始
  ↓
ExecuteAutomaticTranslationLoopAsync() (Line 832)
  ↓ 500msインターバルでループ実行
  ↓
ExecuteAutomaticTranslationStepAsync() (Line 950)
  ↓ _captureService.CaptureWindowAsync(windowHandle)
  ↓
AdaptiveCaptureService.CaptureAsync() (Line 59)
  ↓ GPU環境検出 → 戦略選択 → フォールバック付き実行
  ↓
CaptureStrategyFactory.GetOptimalStrategy() (Line 29)
  ↓ GPU環境に基づく最適戦略選択
  ↓
【選択される戦略（優先順位順）】
  1. DirectFullScreenCaptureStrategy (統合GPU向け)
  2. ROIBasedCaptureStrategy (専用GPU向け)
  3. PrintWindowFallbackStrategy (確実動作保証)
  4. GDIFallbackStrategy (最終手段)
  ↓
WindowsGraphicsCapturer.CaptureWindowAsync()
  ↓ Native DLL呼び出し
  ↓
NativeWindowsCaptureWrapper.CaptureWindow()
  ↓ BaketaCaptureNative.dll (C++/WinRT)
  ↓
画像取得完了 → IImage返却
```

### 1.2 キャプチャ戦略の詳細

#### DirectFullScreenCaptureStrategy
- **対象**: 統合GPU環境
- **優先度**: 最高
- **利点**: 最高効率、メモリ使用量最小
- **実装**: `E:\dev\Baketa\Baketa.Infrastructure.Platform\Windows\Capture\Strategies\DirectFullScreenCaptureStrategy.cs`

#### ROIBasedCaptureStrategy
- **対象**: 専用GPU環境
- **優先度**: 高
- **利点**: ROI（Region of Interest）による部分キャプチャで効率化
- **実装**: `E:\dev\Baketa\Baketa.Infrastructure.Platform\Windows\Capture\Strategies\ROIBasedCaptureStrategy.cs`

#### PrintWindowFallbackStrategy
- **対象**: すべての環境（フォールバック）
- **優先度**: 中
- **利点**: 確実に動作、互換性最高
- **実装**: `E:\dev\Baketa\Baketa.Infrastructure.Platform\Windows\Capture\Strategies\PrintWindowFallbackStrategy.cs`

#### GDIFallbackStrategy
- **対象**: すべての環境（最終手段）
- **優先度**: 最低
- **利点**: Windows GDI使用、古いシステムでも動作
- **実装**: `E:\dev\Baketa\Baketa.Infrastructure.Platform\Windows\Capture\Strategies\GDIFallbackStrategy.cs`

### 1.3 Native DLL実装の重要性

**ファイル**:
- C++側: `E:\dev\Baketa\BaketaCaptureNative\src\WindowsCaptureSession.cpp`
- C#側: `E:\dev\Baketa\Baketa.Infrastructure.Platform\Windows\Capture\NativeWindowsCaptureWrapper.cs`

**技術的背景**:
- .NET 8の`MarshalDirectiveException`を回避するためC++/WinRT実装
- Windows Graphics Capture APIによるDirectX/OpenGLコンテンツキャプチャ
- BGRA pixel formatによる効率的なメモリ処理

**ビルド要件**:
1. Visual Studio 2022でネイティブDLLビルド
2. .NETソリューションビルド
3. DLL配置（自動コピー実装済み）

### 1.4 画像変化検知システム

**実装**: `AdaptiveCaptureService.CaptureAsync()` Line 84-100

```csharp
if (captureResult.Success && captureResult.Images?.Count > 0 &&
    _changeDetectionService != null && _imageAdapter != null)
{
    var coreImage = await _imageAdapter.AdaptToImageAsync(windowsImage).ConfigureAwait(false);
    imageChangeSkipped = await ProcessImageChangeDetectionAsync(
        coreImage, captureRegion).ConfigureAwait(false);
}
```

**効果**: OCR実行回数削減（変化がない場合はスキップ）

---

## 2. OCRフロー調査

### 2.1 フロー概要

```
画像取得完了（IImage）
  ↓
TranslationOrchestrationService.ExecuteTranslationAsync()
  ↓ CoordinateBasedTranslationService呼び出し
  ↓
CoordinateBasedTranslationService.ProcessWithCoordinateBasedTranslationAsync() (Line 137)
  ↓ BatchOcrProcessor.ProcessBatchAsync()
  ↓
BatchOcrProcessor (バッチOCR処理)
  ↓ 画像前処理パイプライン
  ↓
SmartProcessingPipelineService
  ↓ フィルタ適用（4段階）
  ↓
PaddleOcrEngine.RecognizeAsync() (Line 374/390)
  ↓ 画像→Mat変換
  ↓
ConvertToMatAsync() (Line 1484)
  ↓ OCR実行
  ↓
ExecuteOcrAsync() (Line 1772)
  ↓ PaddleOCR PP-OCRv5モデル実行
  ↓
ProcessPaddleResult() → OcrTextRegion生成
  ↓
TextChunk生成（座標情報付き）
```

### 2.2 PaddleOcrEngine.cs 構造分析

**ファイル**: `E:\dev\Baketa\Baketa.Infrastructure\OCR\PaddleOCR\Engine\PaddleOcrEngine.cs`
**行数**: 5,695行
**メソッド数**: 47個

#### メソッド分類

| カテゴリ | メソッド数 | 主要メソッド |
|----------|-----------|-------------|
| **初期化** | 7 | InitializeAsync, WarmupAsync, InitializeEnginesSafelyAsync |
| **OCR実行** | 8 | RecognizeAsync (x2), ExecuteOcrAsync, ExecuteTextDetectionOnlyAsync |
| **画像変換** | 4 | ConvertToMatAsync, ConvertToMatWithScalingAsync, ScaleImageWithLanczos |
| **最適化** | 6 | ApplyJapaneseOptimizations, ApplyEnglishOptimizations |
| **前処理** | 7 | ApplyLocalBrightnessContrast, ApplyAdvancedUnsharpMasking, 等 |
| **後処理** | 2 | ProcessSinglePaddleResult, ProcessPaddleRegion |
| **リソース管理** | 3 | Dispose, DisposeEngines, ThrowIfDisposed |
| **その他** | 10 | 性能統計、エラーハンドリング、等 |

#### 巨大ファイル問題の原因

1. **責務の集中**: 初期化、OCR実行、画像処理、最適化、後処理すべてが1ファイル
2. **言語別最適化**: 日本語、英語それぞれの最適化メソッドが含まれる
3. **複数モデル対応**: PP-OCRv5、レガシーモデルの両対応
4. **前処理パイプライン**: 7つの画像前処理メソッド
5. **エラーハンドリング**: 複雑なタイムアウト、フォールバック処理

### 2.3 推奨リファクタリング計画

#### 分割案: 7-10クラスへの責務分離

```
PaddleOcrEngine (基底クラス)
  ├─ PaddleOcrInitializer (初期化専用)
  ├─ PaddleOcrExecutor (OCR実行専用)
  ├─ PaddleOcrImageConverter (画像変換専用)
  ├─ PaddleOcrPreprocessor (前処理パイプライン)
  ├─ PaddleOcrJapaneseOptimizer (日本語最適化)
  ├─ PaddleOcrEnglishOptimizer (英語最適化)
  ├─ PaddleOcrResultProcessor (後処理専用)
  ├─ PaddleOcrResourceManager (リソース管理)
  └─ PaddleOcrPerformanceMonitor (性能統計)
```

**期待効果**:
- 単一責任原則の徹底
- テスト容易性の向上（各クラス独立テスト可能）
- 保守性の向上（変更の局所化）
- コードレビューの効率化

### 2.4 SmartProcessingPipelineService

**実装**: Phase 1で実装済み
**処理段階**: 4段階の段階的フィルタリング

```
Stage 1: 高速スキップ判定 (画像変化検知)
  ↓
Stage 2: 軽量前処理 (グレースケール変換)
  ↓
Stage 3: 標準前処理 (ノイズ除去、シャープネス)
  ↓
Stage 4: 詳細前処理 (コントラスト調整、二値化)
```

**効果**: 90.5%処理時間削減実現（286ms → 27ms）

---

## 3. 翻訳フロー調査

### 3.1 フロー概要

```
TextChunk生成完了
  ↓
CoordinateBasedTranslationService.ProcessWithCoordinateBasedTranslationAsync()
  ↓ BatchOcrProcessor.ProcessBatchAsync()完了
  ↓
TimedChunkAggregator (時間軸集約システム)
  ↓ 複数チャンクを集約
  ↓
AggregatedChunksReadyEvent発行
  ↓
AggregatedChunksReadyEventHandler.HandleAsync() (Line 157)
  ↓ StreamingTranslationService.TranslateBatchWithStreamingAsync()
  ↓
StreamingTranslationService (ストリーミング翻訳)
  ↓ DefaultTranslationService.TranslateBatchAsync()
  ↓
DefaultTranslationService
  ↓ ActiveEngine.TranslateBatchAsync()
  ↓
OptimizedPythonTranslationEngine.ProcessSingleBatchAsync() (Line 1206)
  ↓ TCP接続プール使用
  ↓
FixedSizeConnectionPool.GetConnectionAsync()
  ↓ Python NLLB-200翻訳サーバーと通信
  ↓
翻訳結果受信 → TranslationResult生成
  ↓
TranslationWithBoundsCompletedEvent発行
```

### 3.2 StreamingTranslationService

**実装**: `E:\dev\Baketa\Baketa.Application\Services\Translation\StreamingTranslationService.cs`

**役割**:
- バッチ翻訳リクエストの分散処理
- ストリーミング形式での翻訳結果配信
- Observable<TranslationResult>によるリアルタイム通知

### 3.3 OptimizedPythonTranslationEngine

**ファイル**: `E:\dev\Baketa\Baketa.Infrastructure\Translation\Local\OptimizedPythonTranslationEngine.cs`
**行数**: 2,765行
**状態**: gRPC移行後に削除予定（Phase 3）

**現在の実装**:
- TCP接続プール（FixedSizeConnectionPool）
- StdinStdout通信モード
- バッチ翻訳最適化
- タイムアウト制御（10秒 → 30秒問題の修正履歴あり）

**技術的負債**:
- 複雑なタイムアウト処理（CLAUDE.local.mdに詳細記載）
- TCP接続プール管理の複雑性
- Phase 12.2の30秒遅延問題（ReadLineAsync）

### 3.4 TimedChunkAggregator

**実装**: `E:\dev\Baketa\Baketa.Infrastructure\OCR\PostProcessing\TimedChunkAggregator.cs`

**役割**:
- 時間軸でのTextChunk集約
- 翻訳品質40-60%向上（CLAUDE.local.mdより）
- AggregatedChunksReadyEvent発行

---

## 4. オーバーレイ表示フロー調査

### 4.1 フロー概要

```
TranslationWithBoundsCompletedEvent発行
  ↓
TranslationWithBoundsCompletedHandler.HandleAsync() (Line 38)
  ↓ PHASE18統一システム vs レガシーシステム判定
  ↓
【PHASE18統一システム】
  ↓
InPlaceTranslationOverlayManager.ShowInPlaceOverlayAsync()
  ↓ TextChunk → InPlaceTranslationOverlayWindow生成
  ↓
InPlaceTranslationOverlayWindow.ShowInPlaceOverlayAsync() (Line 97)
  ↓ Avalonia UIスレッドで実行
  ↓
ウィンドウ位置・サイズ計算
  ↓ GetBasicOverlayPosition(), GetOverlaySize()
  ↓
フォントサイズ最適化
  ↓ CalculateOptimalFontSize()
  ↓
WIDTH_FIX: 横幅固定、縦方向折り返し (Line 124-127)
  ↓
クリックスルー設定 (WS_EX_TRANSPARENT) (Line 146-175)
  ↓
ウィンドウ表示完了
```

### 4.2 PHASE18統一システムの特徴

**ファイル**: `E:\dev\Baketa\Baketa.UI\Services\InPlaceTranslationOverlayManager.cs`

**実装内容**:
- 複数オーバーレイウィンドウの一元管理
- インプレース表示（元テキスト位置に重ね表示）
- クリックスルー（ゲームプレイ阻害防止）
- 自動クリーンアップ

**利点**:
- Google翻訳カメラのようなUX
- ゲームプレイに影響しない
- マルチモニター対応

### 4.3 WIDTH_FIX問題

**発見箇所**: `InPlaceTranslationOverlayWindow.axaml.cs` Line 124-127

```csharp
// 🔧 [TEXT_WRAPPING] ウィンドウサイズ設定: 横幅固定、縦幅は自動調整
// 横幅: OCR検知領域の幅に固定 (テキストが収まらない場合は折り返し)
// 縦幅: SizeToContent="Height" により TextBlock の折り返し後の高さに自動調整
Width = overlaySize.Width;
```

**問題の本質** (推測):
- OCR検知領域の幅が正確に取得できない場合がある
- 翻訳テキストが元テキストより長い場合の横幅計算が不適切
- マルチモニター環境での座標変換問題

**推奨調査事項**:
1. OCR検知領域（CombinedBounds）の精度検証
2. 翻訳テキスト長と横幅の関係分析
3. マルチモニター環境でのテスト
4. GitHubコミット履歴からWIDTH_FIX導入の経緯調査

### 4.4 レガシーシステムとの二重実装

**問題**:
- PHASE18統一システムとレガシーシステムが並存
- 統一システム失敗時のフォールバック実装
- コードの複雑性増加

**推奨対応**:
- 統一システムの安定化後、レガシーシステムの段階的廃止
- フォールバック機構の簡素化

---

## 5. アーキテクチャ評価

### 5.1 Clean Architecture準拠状況

**評価**: ✅ 高度に準拠（5層アーキテクチャ実装済み）

| 層 | 評価 | 詳細 |
|----|------|------|
| **Baketa.Core** | ⭐⭐⭐⭐⭐ | プラットフォーム非依存、抽象化徹底 |
| **Baketa.Infrastructure** | ⭐⭐⭐⭐ | OCR、翻訳の実装、適切な抽象化 |
| **Baketa.Infrastructure.Platform** | ⭐⭐⭐⭐ | Windows固有実装、Adapter Pattern活用 |
| **Baketa.Application** | ⭐⭐⭐ | ビジネスロジック、一部肥大化 |
| **Baketa.UI** | ⭐⭐⭐⭐ | ReactiveUI活用、MVVM準拠 |

### 5.2 設計パターンの活用

| パターン | 使用箇所 | 評価 |
|---------|---------|------|
| **Strategy Pattern** | CaptureStrategyFactory | ⭐⭐⭐⭐⭐ |
| **Adapter Pattern** | WindowsImageAdapter | ⭐⭐⭐⭐ |
| **Factory Pattern** | PaddleOcrEngineFactory | ⭐⭐⭐⭐ |
| **Observer Pattern** | EventAggregator | ⭐⭐⭐⭐⭐ |
| **Repository Pattern** | SettingsService | ⭐⭐⭐ |

### 5.3 依存性注入（DI）システム

**評価**: ⭐⭐⭐⭐⭐ 優れた実装

**特徴**:
- モジュールベースDI（ServiceModuleBase継承）
- 自動循環依存検知
- 優先度ベースモジュールロード
- イベントプロセッサ自動登録

**実装ファイル**:
- `E:\dev\Baketa\Baketa.Application\DI\Modules\ApplicationModule.cs`
- `E:\dev\Baketa\Baketa.Infrastructure\DI\Modules\InfrastructureModule.cs`

---

## 6. 性能最適化の実績

### 6.1 Phase 1実装: 段階的フィルタリングシステム

**成果**: 90.5%処理時間削減（286ms → 27ms）

**実装内容**:
- 4段階処理パイプライン
- Strategy Pattern採用
- Thread-safe実装（ConcurrentDictionary）

**ファイル**: `E:\dev\Baketa\Baketa.Infrastructure\Imaging\SmartProcessingPipelineService.cs`

### 6.2 画像変化検知システム

**効果**: OCR実行回数85%削減（予想値）

**実装**: `AdaptiveCaptureService` + `IImageChangeDetectionService`

### 6.3 GPU環境適応キャプチャ

**効果**: GPU種別に応じた最適戦略選択

**実装**: `CaptureStrategyFactory` + 4つのキャプチャ戦略

---

## 7. 技術的負債と推奨対応

### 7.1 P0: 緊急対応が必要

#### PaddleOcrEngine.cs 巨大ファイル問題

**現状**: 5,695行、47メソッド
**影響**: 保守性低下、テスト困難、コードレビュー非効率

**推奨対応**:
1. 責務分離: 7-10クラスへの分割
2. 単一責任原則の徹底
3. テストカバレッジ向上（現在の1,300+テストケースを各クラスに分散）

**優先度**: P0
**推定工数**: 3-4週間
**リスク**: 低（既存テストカバレッジ高）

### 7.2 P1: 計画的対応が必要

#### OptimizedPythonTranslationEngine削除計画

**現状**: 2,765行、gRPC移行後に削除予定
**影響**: Phase 3完了まで維持コスト

**推奨対応**:
1. Phase 3 gRPC移行完了を確認
2. 段階的廃止（フォールバック維持）
3. 完全削除（統合テスト実施）

**優先度**: P1
**推定工数**: Phase 3依存
**リスク**: 中（移行プロセスの複雑性）

#### WIDTH_FIX問題の根本解決

**現状**: 横幅固定・縦方向折り返しで対応中
**影響**: オーバーレイ表示の完全性

**推奨対応**:
1. GitHubコミット履歴調査（WIDTH_FIX導入経緯）
2. OCR検知領域精度の検証
3. マルチモニター環境テスト
4. 恒久的解決策の実装

**優先度**: P1
**推定工数**: 1-2週間
**リスク**: 中（マルチモニター対応の複雑性）

### 7.3 P2: 長期計画で対応

#### SharpDX依存の解消（WinRTWindowCapture廃止）

**現状**: SharpDX使用、.NET 8互換性問題
**影響**: 将来的な.NETバージョンアップ阻害

**推奨対応**:
1. WinRTWindowCapture廃止計画策定
2. NativeWindowsCaptureWrapperへの完全移行
3. SharpDX依存削除

**優先度**: P2
**推定工数**: 2-3週間
**リスク**: 低（Native DLL実装済み）

---

## 8. Phase 2以降の推奨タスク

### 8.1 短期タスク（1-2ヶ月）

1. **PaddleOcrEngine.cs リファクタリング** (P0)
   - 7-10クラスへの責務分離
   - テストカバレッジ維持
   - ドキュメント更新

2. **WIDTH_FIX問題の根本解決** (P1)
   - 原因調査
   - 恒久的解決策実装
   - マルチモニターテスト

3. **PHASE18統一システムの安定化** (P1)
   - レガシーシステムフォールバック削減
   - エラーハンドリング強化

### 8.2 中期タスク（3-6ヶ月）

1. **OptimizedPythonTranslationEngine削除** (P1)
   - gRPC移行完了確認
   - 段階的廃止実施
   - 統合テスト

2. **SharpDX依存解消** (P2)
   - WinRTWindowCapture廃止
   - Native DLL完全移行

3. **OCR前処理パイプラインの更なる最適化** (P1)
   - GPU並列処理導入
   - VRAM監視統合

### 8.3 長期タスク（6-12ヶ月）

1. **マイクロサービス化検討**
   - OCRサービス分離
   - 翻訳サービス分離
   - gRPC通信統一

2. **クラウド連携強化**
   - Azure/AWS翻訳サービス統合
   - 翻訳キャッシュクラウド化

---

## 9. 重要なコードベース情報

### 9.1 主要ファイルとその役割

| ファイル | 行数 | 役割 | 重要度 |
|---------|------|------|--------|
| `PaddleOcrEngine.cs` | 5,695 | OCRエンジン中核 | ⭐⭐⭐⭐⭐ |
| `OptimizedPythonTranslationEngine.cs` | 2,765 | 翻訳エンジン（削除予定） | ⭐⭐⭐ |
| `TranslationOrchestrationService.cs` | 1,500+ | 翻訳統合管理 | ⭐⭐⭐⭐⭐ |
| `CoordinateBasedTranslationService.cs` | 800+ | 座標ベース翻訳 | ⭐⭐⭐⭐ |
| `AdaptiveCaptureService.cs` | 400+ | 適応的キャプチャ | ⭐⭐⭐⭐ |
| `InPlaceTranslationOverlayWindow.axaml.cs` | 600+ | オーバーレイUI | ⭐⭐⭐⭐ |

### 9.2 テストカバレッジ

**現状**: 1,300+テストケース（CLAUDE.mdより）

**主要テストファイル**:
- `PaddleOcrEngineTests.cs`
- `TranslationOrchestrationServiceTests.cs`
- `AdaptiveCaptureServiceMockTests.cs`
- `CaptureStrategyMockTests.cs`

**推奨**: リファクタリング時にテストカバレッジ維持・向上

### 9.3 設定ファイル

| ファイル | 役割 |
|---------|------|
| `appsettings.json` | メイン設定 |
| `appsettings.Development.json` | 開発環境設定 |
| `appsettings.SentencePiece.json` | レガシー（非推奨） |
| `translation_ports_global.json` | ポート設定 |

---

## 10. まとめと次のステップ

### 10.1 調査成果

1. **全フロー完全理解**: キャプチャ → OCR → 翻訳 → オーバーレイ表示
2. **技術的負債特定**: PaddleOcrEngine巨大化、OptimizedPythonTranslationEngine、WIDTH_FIX
3. **アーキテクチャ評価**: Clean Architecture高準拠、設計パターン活用
4. **性能最適化実績**: Phase 1で90.5%削減達成

### 10.2 推奨アクション（優先順位順）

1. **P0**: PaddleOcrEngine.cs リファクタリング開始（3-4週間）
2. **P1**: WIDTH_FIX問題調査・解決（1-2週間）
3. **P1**: PHASE18統一システム安定化（2-3週間）
4. **P2**: Phase 3完了後のOptimizedPythonTranslationEngine削除計画

### 10.3 期待効果

- **保守性向上**: 巨大ファイル分割により変更容易化
- **開発速度向上**: 単一責任原則による理解容易化
- **品質向上**: テストカバレッジ向上、バグ検出容易化
- **技術的負債削減**: 計画的リファクタリングによる健全化

---

## 付録A: シーケンス図（簡易版）

### A.1 キャプチャから翻訳までの完全フロー

```
User → MainWindowViewModel: Startボタンクリック
MainWindowViewModel → EventAggregator: StartCaptureRequestedEvent
EventAggregator → TranslationFlowEventProcessor: HandleAsync()
TranslationFlowEventProcessor → TranslationOrchestrationService: StartAutomaticTranslationAsync()
TranslationOrchestrationService → CaptureService: CaptureWindowAsync()
CaptureService → AdaptiveCaptureService: CaptureAsync()
AdaptiveCaptureService → CaptureStrategyFactory: GetOptimalStrategy()
CaptureStrategyFactory → NativeWindowsCaptureWrapper: CaptureWindow()
NativeWindowsCaptureWrapper → BaketaCaptureNative.dll: Native Call
BaketaCaptureNative.dll → NativeWindowsCaptureWrapper: Image (BGRA)
NativeWindowsCaptureWrapper → AdaptiveCaptureService: IImage
AdaptiveCaptureService → CoordinateBasedTranslationService: ProcessWithCoordinateBasedTranslationAsync()
CoordinateBasedTranslationService → BatchOcrProcessor: ProcessBatchAsync()
BatchOcrProcessor → SmartProcessingPipelineService: Apply Filters
SmartProcessingPipelineService → PaddleOcrEngine: RecognizeAsync()
PaddleOcrEngine → PaddleOCR: PP-OCRv5 Execution
PaddleOCR → PaddleOcrEngine: OcrTextRegion[]
PaddleOcrEngine → BatchOcrProcessor: TextChunk[]
BatchOcrProcessor → TimedChunkAggregator: Aggregate
TimedChunkAggregator → EventAggregator: AggregatedChunksReadyEvent
EventAggregator → AggregatedChunksReadyEventHandler: HandleAsync()
AggregatedChunksReadyEventHandler → StreamingTranslationService: TranslateBatchWithStreamingAsync()
StreamingTranslationService → OptimizedPythonTranslationEngine: ProcessSingleBatchAsync()
OptimizedPythonTranslationEngine → Python NLLB-200 Server: TCP Request
Python NLLB-200 Server → OptimizedPythonTranslationEngine: Translation Result
OptimizedPythonTranslationEngine → EventAggregator: TranslationWithBoundsCompletedEvent
EventAggregator → TranslationWithBoundsCompletedHandler: HandleAsync()
TranslationWithBoundsCompletedHandler → InPlaceTranslationOverlayManager: ShowInPlaceOverlayAsync()
InPlaceTranslationOverlayManager → InPlaceTranslationOverlayWindow: Display
InPlaceTranslationOverlayWindow → User: オーバーレイ表示完了
```

---

## 付録B: 重要な設定値

### B.1 自動翻訳ループ設定

- **インターバル**: 500ms（最小値、`Translation:AutomaticTranslationIntervalMs`）
- **クールダウン**: 3秒（翻訳完了後、`Translation:PostTranslationCooldownSeconds`）
- **画像変化閾値**: 0.05f（5%）

### B.2 タイムアウト設定

- **OCR タイムアウト**: 15秒（`PaddleOcrEngine`）
- **翻訳タイムアウト**: 10秒 → 30秒（CLAUDE.local.md Phase 12.2参照）
- **接続プールタイムアウト**: 30秒

---

## 付録C: 参考ドキュメント

- `E:\dev\Baketa\CLAUDE.md` - プロジェクト全体ガイド
- `E:\dev\Baketa\CLAUDE.local.md` - 開発履歴（Phase 12.2等）
- `E:\dev\Baketa\docs\OCR_PERFORMANCE_OPTIMIZATION_ROADMAP.md` - OCR最適化ロードマップ
- `E:\dev\Baketa\HYBRID_RESOURCE_MANAGEMENT_DESIGN.md` - Phase 1設計書

---

**レポート作成日**: 2025-10-04
**調査実施者**: Claude Code (UltraThink方法論)
**次回更新**: Phase 2リファクタリング完了後
