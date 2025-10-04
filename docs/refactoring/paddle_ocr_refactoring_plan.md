# PaddleOcrEngine.cs リファクタリング計画

**作成日**: 2025-10-04
**対象ファイル**: E:\dev\Baketa\Baketa.Infrastructure\OCR\PaddleOCR\Engine\PaddleOcrEngine.cs
**現状**: 5,548行、47メソッド
**目標**: Clean Architecture準拠のモジュール化、保守性・テスト容易性の向上

---

## 📊 現状分析

### クラス構造

#### 基本情報
- **総行数**: 5,548行
- **メソッド数**: 47個
- **依存注入フィールド**: 10個
- **private フィールド**: 20個以上
- **責任範囲**: OCR実行、画像処理、モデル管理、エラーハンドリング、パフォーマンス管理など複数の責任を持つ

#### メソッド分類と行数推定

##### 1. 初期化・設定関連（約800行）
- `InitializeAsync` (Line 195-318) - OCRエンジン初期化
- `WarmupAsync` (Line 325-365) - ウォームアップ処理
- `ApplySettingsAsync` (Line 784-838) - 設定適用
- `CheckNativeLibraries` (Line 914-960) - ネイティブライブラリチェック
- `PrepareModelsAsync` (Line 1117-1194) - モデル準備
- `InitializeEnginesSafelyAsync` (Line 1030-1112) - 安全な初期化
- `InitializeHybridModeAsync` (Line 5461以降) - ハイブリッドモード初期化

##### 2. OCR実行コア（約1,500行）
- `RecognizeAsync` (Line 374-768) - メインOCR実行エントリーポイント
- `ExecuteOcrAsync` (Line 1900-2141) - 実際のOCR処理実行
- `ExecuteTextDetectionOnlyAsync` (Line 2147-2229) - 検出専用処理
- `ExecuteDetectionOnlyInternal` (Line 2235-2311) - 内部検出処理
- `ExecuteDetectionOnlyInternalOptimized` (Line 2313-2417) - 最適化版検出処理

##### 3. 画像処理・変換（約1,000行）
- `ConvertToMatAsync` (Line 1484-1499) - IImageからMat変換
- `ConvertToMatWithScalingAsync` - スケーリング付き変換
- `ApplyLocalBrightnessContrast` (Line 3569-3598) - 輝度・コントラスト調整
- `ApplyAdvancedUnsharpMasking` (Line 3598-3639) - アンシャープマスキング
- `ApplyJapaneseOptimizedBinarization` (Line 3639-3672) - 日本語最適化二値化
- `ApplyJapaneseOptimizedMorphology` (Line 3672-3707) - 日本語最適化モルフォロジー
- `ApplyFinalQualityEnhancement` (Line 3707以降) - 最終品質向上
- `NormalizeImageDimensions` (Line 4835以降) - 画像サイズ正規化
- `ValidateMatForPaddleOCR` (Line 4695以降) - Mat検証
- `ApplyPreventiveNormalization` (Line 5568以降) - 予防的正規化

##### 4. 結果処理（約800行）
- `ConvertPaddleOcrResult` - PaddleOCR結果変換
- `ProcessSinglePaddleResult` (Line 2775以降) - 単一結果処理
- `ProcessPaddleRegion` (Line 2886以降) - テキスト領域処理
- `ConvertDetectionOnlyResult` (Line 2423-2484) - 検出専用結果変換
- `ProcessSinglePaddleResultForDetectionOnly` (Line 2489以降) - 検出専用単一結果処理
- `CreateEmptyResult` (Line 3129-3144) - 空結果作成
- `CalculateBoundingBoxFromRegion` (Line 2659以降) - バウンディングボックス計算

##### 5. パフォーマンス・統計（約300行）
- `GetPerformanceStats` (Line 886-907) - パフォーマンス統計取得
- `UpdatePerformanceStats` (Line 3144以降) - 統計更新
- `CalculateBaseTimeout` (Line 4560以降) - 基本タイムアウト計算
- `GetAdaptiveTimeout` (Line 4654以降) - 適応的タイムアウト計算
- `CancelCurrentOcrTimeout` (Line 4387以降) - タイムアウトキャンセル

##### 6. エラーハンドリング（約500行）
- `CollectPaddlePredictorErrorInfo` (Line 5094以降) - エラー情報収集
- `GeneratePaddleErrorSuggestion` (Line 5205以降) - エラー解決策生成
- `ResetFailureCounter` (Line 5548以降) - 失敗カウンタリセット
- `GetConsecutiveFailureCount` (Line 5559以降) - 連続失敗数取得

##### 7. モデル管理（約500行）
- `TryCreatePPOCRv5ModelAsync` (Line 1199-1238) - PP-OCRv5モデル作成試行
- `CreatePPOCRv5CustomModelAsync` (Line 1243-1384) - カスタムモデル作成
- `GetPPOCRv5RecognitionModelPath` (Line 1389-1399) - PP-OCRv5パス取得
- `GetPPOCRv5Model` (Line 1404-1427) - PP-OCRv5モデル取得
- `GetDefaultLocalModel` (Line 1432-1469) - デフォルトモデル取得
- `GetRecognitionModelName` (Line 1474-1479) - 認識モデル名取得
- `DetectIfV5Model` (Line 5273以降) - V5モデル検出

##### 8. 最適化・設定適用（約400行）
- `ApplyJapaneseOptimizations` (Line 3199以降) - 日本語最適化
- `ApplyEnglishOptimizations` (Line 3226以降) - 英語最適化
- `ApplyDetectionOptimization` (Line 5361以降) - 検出最適化
- `DetermineLanguageFromSettings` (Line 3254以降) - 言語決定
- `MapDisplayNameToLanguageCode` (Line 3359以降) - 言語コードマッピング
- `IsJapaneseLanguage` (Line 3425以降) - 日本語判定
- `DetermineProcessingMode` (Line 5539以降) - 処理モード決定

##### 9. リソース管理（約200行）
- `Dispose` (Line 3558以降) - リソース破棄
- `DisposeEngines` (Line 3165以降) - エンジン破棄
- `CheckGpuMemoryLimitsAsync` (Line 3436以降) - GPUメモリ制限チェック
- `EstimateRequiredGpuMemory` (Line 3486以降) - 必要GPUメモリ推定
- `ThrowIfDisposed` (Line 3506以降) - 破棄済みチェック
- `ThrowIfNotInitialized` (Line 3184以降) - 未初期化チェック

##### 10. ユーティリティ（約200行）
- `IsTestEnvironment` (Line 965-1025) - テスト環境判定
- `CreateDummyMat` (Line 1708以降) - ダミーMat作成
- `GetDebugLogPath` (Line 5319以降) - デバッグログパス取得
- `SafeWriteDebugLog` (Line 5344以降) - 安全なデバッグログ書き込み
- `SelectOptimalGameProfile` (Line 5292以降) - 最適ゲームプロファイル選択

### 依存関係

#### 外部依存（ライブラリ）
- `Sdcb.PaddleOCR` - PaddleOCRライブラリ本体
- `OpenCvSharp` - 画像処理ライブラリ
- `Microsoft.Extensions.Logging` - ロギング
- `Microsoft.Extensions.DependencyInjection` - DI

#### 内部依存（Baketa内部）
- `IModelPathResolver` - モデルパス解決
- `IOcrPreprocessingService` - OCR前処理
- `ITextMerger` - テキスト結合
- `IOcrPostProcessor` - OCR後処理
- `IGpuMemoryManager` - GPUメモリ管理
- `IUnifiedSettingsService` - 設定サービス
- `IEventAggregator` - イベント集約
- `IImageFactory` - 画像ファクトリ

#### 被依存（このクラスを使用している箇所）
- `PooledOcrService` - オブジェクトプール経由での使用
- `HybridPaddleOcrService` - ハイブリッドOCR戦略
- `AdaptiveOcrEngine` - 適応的OCRラッパー
- `IntelligentFallbackOcrEngine` - フォールバック機能付きラッパー
- `StickyRoiOcrEngineWrapper` - ROI固定機能付きラッパー
- `EnsembleOcrEngine` - アンサンブルOCR
- 多数のテストクラス

---

## 🎯 リファクタリング方針

### 基本原則

1. **Single Responsibility Principle（単一責任の原則）**
   - 各クラスは1つの明確な責任のみを持つ
   - 変更理由が1つになるようにする

2. **Clean Architecture準拠**
   - Infrastructure層の適切な分離
   - インターフェース経由の疎結合
   - テスタビリティの向上

3. **段階的リファクタリング**
   - 既存機能を壊さない
   - 後方互換性の維持（可能な限り）
   - 各フェーズでビルド・テストが成功すること

### 分割後のクラス設計

#### 1. PaddleOcrEngine（コア実装） - 推定800行
**責任**: OCR実行のオーケストレーション、IOcrEngineインターフェース実装

**主要メソッド**:
- `InitializeAsync` - 初期化のオーケストレーション
- `RecognizeAsync` - OCR実行のメインエントリーポイント
- `GetSettings` / `ApplySettingsAsync` - 設定管理
- `GetPerformanceStats` - パフォーマンス統計

**保持するフィールド**:
- 各サービスへの参照（DI経由）
- 基本的なステート（IsInitialized, CurrentLanguageなど）

**特徴**:
- 実際の処理は各専門サービスに委譲
- Facade パターンによる統一インターフェース提供

---

#### 2. PaddleOcrImageProcessor（新規） - 推定1,200行
**責任**: 画像処理・前処理・変換

**主要メソッド**:
- `ConvertToMatAsync` - IImageからMat変換
- `ConvertToMatWithScalingAsync` - スケーリング付き変換
- `ApplyBrightnessContrast` - 輝度・コントラスト調整
- `ApplyUnsharpMasking` - アンシャープマスキング
- `ApplyJapaneseOptimizations` - 日本語最適化前処理
- `ApplyEnglishOptimizations` - 英語最適化前処理
- `NormalizeImageDimensions` - 画像サイズ正規化
- `ValidateMat` - Mat検証
- `ApplyPreventiveNormalization` - 予防的正規化

**インターフェース**:
```csharp
public interface IPaddleOcrImageProcessor
{
    Task<Mat> ConvertToMatAsync(IImage image, Rectangle? roi, CancellationToken cancellationToken);
    Task<(Mat mat, double scaleFactor)> ConvertToMatWithScalingAsync(IImage image, Rectangle? roi, CancellationToken cancellationToken);
    Mat ApplyLanguageOptimizations(Mat inputMat, string language);
    Mat NormalizeImageDimensions(Mat inputMat);
    bool ValidateMat(Mat mat);
    Mat ApplyPreventiveNormalization(Mat inputMat);
}
```

**依存**:
- OpenCvSharp
- ILogger
- IUnifiedSettingsService（設定取得用）

---

#### 3. PaddleOcrExecutor（新規） - 推定1,000行
**責任**: 実際のPaddleOCR実行、タイムアウト管理、リトライ処理

**主要メソッド**:
- `ExecuteOcrAsync` - OCR実行
- `ExecuteDetectionOnlyAsync` - 検出専用実行
- `ExecuteWithTimeout` - タイムアウト付き実行
- `ExecuteWithRetry` - リトライ付き実行
- `CalculateTimeout` - タイムアウト計算
- `HandlePaddleError` - PaddleOCRエラーハンドリング

**インターフェース**:
```csharp
public interface IPaddleOcrExecutor
{
    Task<PaddleOcrResult[]> ExecuteOcrAsync(Mat processedMat, IProgress<OcrProgress>? progress, CancellationToken cancellationToken);
    Task<PaddleOcrResult[]> ExecuteDetectionOnlyAsync(Mat processedMat, CancellationToken cancellationToken);
    void CancelCurrentOcrTimeout();
}
```

**依存**:
- `PaddleOcrAll` / `QueuedPaddleOcrAll`
- ILogger
- パフォーマンス統計サービス

---

#### 4. PaddleOcrResultConverter（新規） - 推定800行
**責任**: PaddleOCR結果の変換、座標復元、テキスト結合

**主要メソッド**:
- `ConvertPaddleOcrResult` - PaddleOCR結果をOcrTextRegionに変換
- `ProcessSinglePaddleResult` - 単一結果処理
- `ProcessPaddleRegion` - テキスト領域処理
- `CalculateBoundingBox` - バウンディングボックス計算
- `AdjustCoordinatesForRoi` - ROI座標補正
- `RestoreCoordinatesForScaling` - スケーリング座標復元
- `CreateEmptyResult` - 空結果作成

**インターフェース**:
```csharp
public interface IPaddleOcrResultConverter
{
    IReadOnlyList<OcrTextRegion> ConvertToTextRegions(PaddleOcrResult[] paddleResults, double scaleFactor, Rectangle? roi);
    IReadOnlyList<OcrTextRegion> ConvertDetectionOnlyResult(PaddleOcrResult[] paddleResults);
    OcrResults CreateEmptyResult(IImage image, Rectangle? roi, TimeSpan processingTime);
}
```

**依存**:
- CoordinateRestorer（既存）
- ITextMerger（既存）
- IOcrPostProcessor（既存）
- ILogger

---

#### 5. PaddleOcrModelManager（新規） - 推定600行
**責任**: モデル管理、モデルロード、モデル選択

**主要メソッド**:
- `PrepareModelsAsync` - モデル準備
- `TryCreatePPOCRv5ModelAsync` - PP-OCRv5モデル作成
- `CreateCustomModelAsync` - カスタムモデル作成
- `GetModelForLanguage` - 言語別モデル取得
- `DetectModelVersion` - モデルバージョン検出
- `GetRecognitionModelPath` - 認識モデルパス取得

**インターフェース**:
```csharp
public interface IPaddleOcrModelManager
{
    Task<FullOcrModel?> PrepareModelsAsync(string language, CancellationToken cancellationToken);
    Task<FullOcrModel?> TryCreatePPOCRv5ModelAsync(string language, CancellationToken cancellationToken);
    FullOcrModel? GetDefaultModelForLanguage(string language);
    bool DetectIfV5Model(FullOcrModel model);
}
```

**依存**:
- IModelPathResolver（既存）
- PPOCRv5ModelProvider（既存）
- LocalFullModels（Sdcb.PaddleOCR）
- ILogger

---

#### 6. PaddleOcrEngineInitializer（新規） - 推定400行
**責任**: PaddleOcrAllエンジンの初期化、設定適用、ウォームアップ

**主要メソッド**:
- `InitializeEnginesSafelyAsync` - 安全な初期化
- `ApplyOptimizationParameters` - 最適化パラメータ適用
- `WarmupAsync` - ウォームアップ
- `CheckNativeLibraries` - ネイティブライブラリチェック
- `ReinitializeEngineAsync` - 再初期化

**インターフェース**:
```csharp
public interface IPaddleOcrEngineInitializer
{
    Task<bool> InitializeEnginesAsync(FullOcrModel models, OcrEngineSettings settings, CancellationToken cancellationToken);
    Task<bool> WarmupAsync(CancellationToken cancellationToken);
    bool CheckNativeLibraries();
    PaddleOcrAll? GetOcrEngine();
    QueuedPaddleOcrAll? GetQueuedEngine();
}
```

**依存**:
- Sdcb.PaddleOCR
- OpenCvSharp
- ILogger
- IGpuMemoryManager

---

#### 7. PaddleOcrPerformanceTracker（新規） - 推定300行
**責任**: パフォーマンス統計、タイムアウト管理、エラー追跡

**主要メソッド**:
- `UpdatePerformanceStats` - 統計更新
- `GetPerformanceStats` - 統計取得
- `CalculateBaseTimeout` - 基本タイムアウト計算
- `GetAdaptiveTimeout` - 適応的タイムアウト
- `ResetFailureCounter` - 失敗カウンタリセット
- `IncrementFailureCounter` - 失敗カウンタ増加

**インターフェース**:
```csharp
public interface IPaddleOcrPerformanceTracker
{
    void UpdatePerformanceStats(double processingTimeMs, bool success);
    OcrPerformanceStats GetPerformanceStats();
    int CalculateTimeout(Mat mat);
    int GetAdaptiveTimeout(int baseTimeout);
    void ResetFailureCounter();
    int GetConsecutiveFailureCount();
}
```

**依存**:
- ConcurrentQueue（統計保持）
- ILogger

---

#### 8. PaddleOcrErrorHandler（新規） - 推定500行
**責任**: エラー診断、エラーメッセージ生成、解決策提案

**主要メソッド**:
- `CollectErrorInfo` - エラー情報収集
- `GenerateErrorSuggestion` - 解決策生成
- `HandlePaddlePredictorError` - PaddlePredictorエラー処理
- `HandleMemoryError` - メモリエラー処理
- `HandleTimeoutError` - タイムアウトエラー処理

**インターフェース**:
```csharp
public interface IPaddleOcrErrorHandler
{
    string CollectErrorInfo(Mat mat, Exception ex);
    string GenerateErrorSuggestion(string errorMessage);
    Task<bool> TryRecoverFromError(Exception ex, Func<Task<bool>> retryAction);
}
```

**依存**:
- ILogger
- IEventAggregator（診断イベント発行用）

---

#### 9. PaddleOcrLanguageOptimizer（新規） - 推定400行
**責任**: 言語別最適化、言語判定、パラメータ調整

**主要メソッド**:
- `DetermineLanguage` - 言語決定
- `MapDisplayNameToLanguageCode` - 言語コードマッピング
- `ApplyLanguageOptimizations` - 言語別最適化
- `SelectOptimalProfile` - 最適プロファイル選択
- `IsJapaneseLanguage` - 日本語判定

**インターフェース**:
```csharp
public interface IPaddleOcrLanguageOptimizer
{
    string DetermineLanguageFromSettings(OcrEngineSettings settings);
    string MapDisplayNameToLanguageCode(string displayName);
    void ApplyLanguageOptimizations(PaddleOcrAll engine, string language);
    string SelectOptimalGameProfile(ImageCharacteristics characteristics);
}
```

**依存**:
- IUnifiedSettingsService
- ILogger

---

#### 10. PaddleOcrUtilities（新規） - 推定200行
**責任**: ユーティリティメソッド、テスト環境判定、ログ出力

**主要メソッド**:
- `IsTestEnvironment` - テスト環境判定
- `CreateDummyMat` - ダミーMat作成
- `GetDebugLogPath` - デバッグログパス取得
- `SafeWriteDebugLog` - 安全なログ書き込み

**インターフェース**:
```csharp
public interface IPaddleOcrUtilities
{
    bool IsTestEnvironment();
    Mat CreateDummyMat();
    string GetDebugLogPath();
    void SafeWriteDebugLog(string message);
}
```

---

### インターフェース設計原則

1. **疎結合の実現**
   - すべての主要機能をインターフェース経由で提供
   - モック可能な設計でテスト容易性向上

2. **Dependency Injection対応**
   - すべてのサービスクラスがDI登録可能
   - ライフタイム管理の明確化（Singleton vs Scoped）

3. **非同期対応**
   - すべてのI/O操作は非同期メソッド
   - CancellationToken対応

---

## 📋 段階的実装計画

### Phase 2.1: 基盤準備とインターフェース定義（所要時間: 2-3日）

#### タスク
- [ ] 全インターフェース定義を作成（`Abstractions`フォルダ配下）
  - `IPaddleOcrImageProcessor.cs`
  - `IPaddleOcrExecutor.cs`
  - `IPaddleOcrResultConverter.cs`
  - `IPaddleOcrModelManager.cs`
  - `IPaddleOcrEngineInitializer.cs`
  - `IPaddleOcrPerformanceTracker.cs`
  - `IPaddleOcrErrorHandler.cs`
  - `IPaddleOcrLanguageOptimizer.cs`
  - `IPaddleOcrUtilities.cs`

- [ ] ディレクトリ構造作成
  ```
  Baketa.Infrastructure/OCR/PaddleOCR/
    ├── Engine/
    │   └── PaddleOcrEngine.cs（既存）
    ├── Services/
    │   ├── PaddleOcrImageProcessor.cs（新規）
    │   ├── PaddleOcrExecutor.cs（新規）
    │   ├── PaddleOcrResultConverter.cs（新規）
    │   ├── PaddleOcrModelManager.cs（新規）
    │   ├── PaddleOcrEngineInitializer.cs（新規）
    │   ├── PaddleOcrPerformanceTracker.cs（新規）
    │   ├── PaddleOcrErrorHandler.cs（新規）
    │   ├── PaddleOcrLanguageOptimizer.cs（新規）
    │   └── PaddleOcrUtilities.cs（新規）
    └── Abstractions/
        └── （上記インターフェース）
  ```

- [ ] 基本的なDTOクラス作成（必要に応じて）
  - `ImageProcessingOptions.cs`
  - `OcrExecutionContext.cs`

#### 期待成果
- すべてのインターフェースが定義済み
- ディレクトリ構造が準備済み
- ビルドが成功（実装は空でも可）

---

### Phase 2.2: ユーティリティ・パフォーマンストラッカー実装（所要時間: 2日）

#### タスク
- [ ] `PaddleOcrUtilities` 実装
  - `IsTestEnvironment` 移動
  - `CreateDummyMat` 移動
  - `GetDebugLogPath` / `SafeWriteDebugLog` 移動

- [ ] `PaddleOcrPerformanceTracker` 実装
  - パフォーマンス統計フィールド移動
  - `UpdatePerformanceStats` 移動
  - `GetPerformanceStats` 移動
  - `CalculateBaseTimeout` / `GetAdaptiveTimeout` 移動
  - 失敗カウンター関連メソッド移動

- [ ] DI登録
  - `PaddleOcrModule.cs` にサービス登録追加
  - Singletonライフタイム指定

- [ ] 単体テスト作成
  - `PaddleOcrUtilitiesTests.cs`
  - `PaddleOcrPerformanceTrackerTests.cs`

#### 期待成果
- ユーティリティとパフォーマンストラッカーが独立クラスとして動作
- 既存のPaddleOcrEngineから参照可能
- テストが成功

---

### Phase 2.3: エラーハンドラー・言語最適化実装（所要時間: 3日）

#### タスク
- [ ] `PaddleOcrErrorHandler` 実装
  - `CollectPaddlePredictorErrorInfo` 移動
  - `GeneratePaddleErrorSuggestion` 移動
  - エラーリカバリーロジック追加

- [ ] `PaddleOcrLanguageOptimizer` 実装
  - `DetermineLanguageFromSettings` 移動
  - `MapDisplayNameToLanguageCode` 移動
  - `ApplyJapaneseOptimizations` 移動
  - `ApplyEnglishOptimizations` 移動
  - `SelectOptimalGameProfile` 移動

- [ ] DI登録とテスト

#### 期待成果
- エラーハンドリングロジックが分離
- 言語最適化ロジックが分離
- PaddleOcrEngineから該当コードが削除可能

---

### Phase 2.4: モデルマネージャー実装（所要時間: 3-4日）

#### タスク
- [ ] `PaddleOcrModelManager` 実装
  - `PrepareModelsAsync` 移動
  - `TryCreatePPOCRv5ModelAsync` 移動
  - `CreatePPOCRv5CustomModelAsync` 移動
  - `GetPPOCRv5RecognitionModelPath` 移動
  - `GetPPOCRv5Model` 移動
  - `GetDefaultLocalModel` 移動
  - `GetRecognitionModelName` 移動
  - `DetectIfV5Model` 移動

- [ ] モデルキャッシュ機構追加（オプション）
  - ロード済みモデルの再利用
  - メモリ効率化

- [ ] DI登録とテスト

#### 期待成果
- モデル管理ロジックが完全に分離
- PaddleOcrEngineがモデルマネージャー経由でモデル取得
- 約500行のコードがPaddleOcrEngineから削除

---

### Phase 2.5: 画像プロセッサー実装（所要時間: 4-5日）

#### タスク
- [ ] `PaddleOcrImageProcessor` 実装
  - `ConvertToMatAsync` 移動
  - `ConvertToMatWithScalingAsync` 移動
  - `ApplyLocalBrightnessContrast` 移動
  - `ApplyAdvancedUnsharpMasking` 移動
  - `ApplyJapaneseOptimizedBinarization` 移動
  - `ApplyJapaneseOptimizedMorphology` 移動
  - `ApplyFinalQualityEnhancement` 移動
  - `NormalizeImageDimensions` 移動
  - `ValidateMatForPaddleOCR` 移動
  - `ApplyPreventiveNormalization` 移動

- [ ] パイプライン設計の見直し
  - フィルターチェーンパターン適用検討
  - 前処理ステップの動的設定

- [ ] DI登録とテスト
  - 画像処理テストケース充実化
  - パフォーマンステスト

#### 期待成果
- 画像処理ロジックが完全に分離
- 約1,000行のコードがPaddleOcrEngineから削除
- 画像処理フローの可読性向上

---

### Phase 2.6: エンジン初期化実装（所要時間: 3日）

#### タスク
- [ ] `PaddleOcrEngineInitializer` 実装
  - `InitializeEnginesSafelyAsync` 移動
  - `CheckNativeLibraries` 移動
  - `WarmupAsync` 移動
  - `ReinitializeEngineAsync` 移動
  - エンジンライフサイクル管理

- [ ] 設定適用ロジック最適化
  - `ApplyDetectionOptimization` 統合

- [ ] DI登録とテスト

#### 期待成果
- 初期化ロジックが分離
- PaddleOcrAllエンジンの管理が明確化

---

### Phase 2.7: OCR実行エグゼキューター実装（所要時間: 4-5日）

#### タスク
- [ ] `PaddleOcrExecutor` 実装
  - `ExecuteOcrAsync` 移動
  - `ExecuteTextDetectionOnlyAsync` 移動
  - `ExecuteDetectionOnlyInternal` 移動
  - `ExecuteDetectionOnlyInternalOptimized` 移動
  - `ExecuteOcrInSeparateTask` 移動（存在する場合）
  - `ExecuteOcrInSeparateTaskOptimized` 移動（存在する場合）
  - タイムアウト・リトライロジック統合

- [ ] エラーハンドリング統合
  - `PaddleOcrErrorHandler`と連携

- [ ] パフォーマンストラッキング統合
  - `PaddleOcrPerformanceTracker`と連携

- [ ] DI登録とテスト

#### 期待成果
- OCR実行ロジックが完全に分離
- 約1,500行のコードがPaddleOcrEngineから削除
- 実行フローの可読性向上

---

### Phase 2.8: 結果コンバーター実装（所要時間: 3-4日）

#### タスク
- [ ] `PaddleOcrResultConverter` 実装
  - `ConvertPaddleOcrResult` 移動
  - `ProcessSinglePaddleResult` 移動
  - `ProcessPaddleRegion` 移動
  - `ConvertDetectionOnlyResult` 移動
  - `ProcessSinglePaddleResultForDetectionOnly` 移動
  - `CalculateBoundingBoxFromRegion` 移動
  - `AdjustCoordinatesForRoi` 移動（存在する場合）
  - `CreateEmptyResult` 移動

- [ ] 座標復元ロジック統合
  - CoordinateRestorerとの連携強化

- [ ] テキスト結合・後処理統合
  - ITextMerger / IOcrPostProcessorとの連携

- [ ] DI登録とテスト

#### 期待成果
- 結果変換ロジックが完全に分離
- 約800行のコードがPaddleOcrEngineから削除
- 変換処理の可読性向上

---

### Phase 2.9: PaddleOcrEngineリファクタリング（所要時間: 3-4日）

#### タスク
- [ ] PaddleOcrEngine本体のリファクタリング
  - すべての実装ロジックを各サービスへの委譲に変更
  - Facadeパターンの完全実装
  - コメント・ログの整理

- [ ] DI注入フィールドの整理
  - 新規サービスへの依存追加
  - 不要な依存削除

- [ ] IOcrEngineインターフェース実装の最適化
  - 各メソッドがサービス呼び出しのみになるよう簡素化

- [ ] イベント発行の整理
  - 診断イベント発行の一元化

#### 期待成果
- PaddleOcrEngineが約800行に削減
- 各メソッドが明確な責任を持つ
- 可読性・保守性が大幅向上

---

### Phase 2.10: 統合テスト・ドキュメント整備（所要時間: 3日）

#### タスク
- [ ] 統合テスト作成
  - リファクタリング前後での動作同一性確認
  - すべてのOCRパイプラインテスト
  - エッジケーステスト

- [ ] パフォーマンステスト
  - リファクタリング前後のパフォーマンス比較
  - メモリ使用量確認

- [ ] ドキュメント更新
  - アーキテクチャ図作成
  - 各サービスの責任範囲説明
  - 使用例・移行ガイド作成

- [ ] コードレビュー
  - Geminiによる最終レビュー
  - 指摘事項の修正

#### 期待成果
- すべてのテストが成功
- パフォーマンス劣化がないこと確認
- ドキュメントが完備

---

## ⚠️ リスク評価

### 影響範囲

#### 直接変更が必要なファイル
1. **PaddleOcrEngine.cs** - 完全リファクタリング
2. **PaddleOcrModule.cs** - DI登録追加
3. **既存のラッパー・アダプタークラス**
   - `PooledOcrService.cs` - 初期化ロジック調整が必要な可能性
   - `HybridPaddleOcrService.cs` - サービス注入方法変更
   - `AdaptiveOcrEngine.cs` - 依存関係調整
   - `IntelligentFallbackOcrEngine.cs` - エラーハンドリング統合
   - その他のラッパークラス（必要に応じて）

#### テスト修正が必要な箇所
1. **PaddleOcrEngineTests.cs** - モック対象変更
2. **統合テスト全般** - サービス注入方法変更
3. **新規テストクラス作成**（約10ファイル）

### 主要リスクと軽減策

#### リスク1: 既存機能の破壊
**発生確率**: 中
**影響度**: 高

**軽減策**:
- Phase完了ごとに既存テストを実行
- 各Phase終了時点でビルドが成功することを確認
- リファクタリング前のテストスイートを完全に維持
- 統合テストで動作同一性を保証

#### リスク2: パフォーマンス劣化
**発生確率**: 低
**影響度**: 中

**軽減策**:
- サービス呼び出しのオーバーヘッド最小化
- インライン化可能なメソッドは検討
- パフォーマンステストの実施
- 必要に応じてベンチマーク実行

#### リスク3: DI循環参照
**発生確率**: 低
**影響度**: 中

**軽減策**:
- サービス間の依存関係を明確化
- 循環参照が発生しない設計を事前確認
- 必要に応じてイベント駆動設計を活用

#### リスク4: テストコスト増大
**発生確率**: 高
**影響度**: 低

**軽減策**:
- モッキング容易な設計（インターフェース経由）
- テストヘルパークラスの充実化
- 段階的なテスト作成（Phase進行と並行）

#### リスク5: 後方互換性の喪失
**発生確率**: 低
**影響度**: 中

**軽減策**:
- IOcrEngineインターフェースは変更しない
- 既存のpublicメソッドシグネチャは維持
- ラッパークラス経由での移行パス提供

---

## ✅ 期待効果

### コード品質向上

| 指標 | リファクタリング前 | リファクタリング後 | 改善率 |
|------|-------------------|-------------------|--------|
| **PaddleOcrEngine.cs 行数** | 5,548行 | 約800行 | **-85.6%** |
| **平均メソッド行数** | 約118行 | 約50行 | **-57.6%** |
| **クラスの責任数** | 10個 | 1個 | **-90%** |
| **単体テスト容易性** | 困難 | 容易 | **大幅向上** |
| **循環的複雑度** | 非常に高い | 低い | **大幅改善** |

### 保守性向上

- **変更影響範囲の局所化**
  - 画像処理変更 → `PaddleOcrImageProcessor`のみ
  - エラーハンドリング変更 → `PaddleOcrErrorHandler`のみ
  - モデル管理変更 → `PaddleOcrModelManager`のみ

- **コード理解の容易化**
  - 各クラスが明確な1つの責任を持つ
  - 依存関係が明示的（インターフェース経由）
  - ドキュメントが充実

- **テストの容易化**
  - モック作成が容易
  - 独立したテストが可能
  - テストカバレッジ向上

### 拡張性向上

- **新機能追加の容易化**
  - 新しい画像処理フィルター追加 → `PaddleOcrImageProcessor`に追加
  - 新しい言語最適化 → `PaddleOcrLanguageOptimizer`に追加
  - 新しいエラーハンドリング → `PaddleOcrErrorHandler`に追加

- **プラグイン化の可能性**
  - 各サービスが独立しているため、プラグインとして分離可能
  - カスタム実装の差し替えが容易

### Clean Architecture準拠

- **依存関係の逆転**
  - すべての依存がインターフェース経由
  - Infrastructure層内部でのクリーンな分離

- **テスタビリティ**
  - すべてのサービスがモック可能
  - 単体テスト・統合テストの両方が容易

- **関心の分離**
  - 各クラスが明確な責任範囲を持つ
  - Single Responsibility Principleの遵守

---

## 📝 実装ガイドライン

### コーディング規約

1. **C# 12機能の活用**
   - File-scoped namespaces
   - Primary constructors（シンプルなクラスで使用）
   - Collection expressions `[]`
   - Pattern matching

2. **非同期プログラミング**
   - `ConfigureAwait(false)` を必須使用（ライブラリコード）
   - CancellationToken の適切な伝播
   - Task.Run の適切な使用

3. **エラーハンドリング**
   - カスタム例外クラスの使用
   - ログ出力の統一
   - 診断イベントの発行

4. **ログ出力**
   - ILogger経由での構造化ログ
   - 適切なログレベル設定
   - パフォーマンス影響の最小化

### テスト戦略

1. **単体テスト**
   - 各サービスクラスに対応するテストクラス作成
   - モックを活用した独立テスト
   - エッジケース・異常系テストの充実化

2. **統合テスト**
   - リファクタリング前後での動作同一性確認
   - 実際のPaddleOCRエンジンを使用したテスト
   - パフォーマンステスト

3. **テストカバレッジ**
   - 目標: 80%以上
   - クリティカルパスは100%カバー

### DI登録例

```csharp
// PaddleOcrModule.cs
public class PaddleOcrModule : ServiceModuleBase
{
    protected override void Load(IServiceCollection services)
    {
        // 新規サービス登録（すべてSingleton）
        services.AddSingleton<IPaddleOcrUtilities, PaddleOcrUtilities>();
        services.AddSingleton<IPaddleOcrPerformanceTracker, PaddleOcrPerformanceTracker>();
        services.AddSingleton<IPaddleOcrErrorHandler, PaddleOcrErrorHandler>();
        services.AddSingleton<IPaddleOcrLanguageOptimizer, PaddleOcrLanguageOptimizer>();
        services.AddSingleton<IPaddleOcrModelManager, PaddleOcrModelManager>();
        services.AddSingleton<IPaddleOcrEngineInitializer, PaddleOcrEngineInitializer>();
        services.AddSingleton<IPaddleOcrImageProcessor, PaddleOcrImageProcessor>();
        services.AddSingleton<IPaddleOcrExecutor, PaddleOcrExecutor>();
        services.AddSingleton<IPaddleOcrResultConverter, PaddleOcrResultConverter>();

        // 既存のPaddleOcrEngine（依存が変更される）
        services.AddSingleton<IOcrEngine, PaddleOcrEngine>();
    }
}
```

### 段階的移行パターン

#### パターン1: Extract Class + Delegate
```csharp
// Before
public class PaddleOcrEngine
{
    public void SomeMethod()
    {
        // 複雑なロジック
    }
}

// After (Phase 1)
public class PaddleOcrEngine
{
    private readonly INewService _newService;

    public void SomeMethod()
    {
        _newService.ExecuteLogic(); // 委譲
    }
}

public class NewService : INewService
{
    public void ExecuteLogic()
    {
        // 移動された複雑なロジック
    }
}
```

#### パターン2: Interface Extraction
```csharp
// Before
public class PaddleOcrEngine
{
    private void PrivateHelperMethod() { }
}

// After
public interface IHelper
{
    void HelperMethod();
}

public class PaddleOcrEngine
{
    private readonly IHelper _helper;
}

public class Helper : IHelper
{
    public void HelperMethod() { /* 元のprivateメソッド */ }
}
```

---

## 📚 参考資料

### Clean Architecture関連
- [Clean Architecture by Robert C. Martin](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [.NET Clean Architecture Template](https://github.com/jasontaylordev/CleanArchitecture)

### リファクタリング手法
- [Refactoring by Martin Fowler](https://refactoring.com/)
- [Extract Class Refactoring](https://refactoring.guru/extract-class)

### Baketaプロジェクト内参考実装
- `Baketa.Core/Services/Imaging/SmartProcessingPipelineService.cs` - 優れた段階的フィルタリング設計
- `Baketa.Infrastructure/Translation/` - サービス分離の良い例

---

## 🎯 成功基準

### Phase完了基準
各Phaseは以下の条件を満たして完了とする：

1. ✅ **ビルド成功**: エラー0件、警告は最小限
2. ✅ **既存テスト成功**: リファクタリング前のテストがすべて成功
3. ✅ **新規テスト作成**: 新規クラスに対応するテストが作成済み
4. ✅ **コードレビュー**: Geminiによるレビュー完了、指摘事項対応済み
5. ✅ **ドキュメント更新**: 変更内容がドキュメント化済み

### 最終完了基準

1. ✅ **PaddleOcrEngine.cs**: 800行以下に削減
2. ✅ **すべての新規サービスクラス**: 実装・テスト完了
3. ✅ **統合テスト**: すべて成功
4. ✅ **パフォーマンステスト**: リファクタリング前と同等以上
5. ✅ **ドキュメント**: 完全整備済み
6. ✅ **コードレビュー**: 最終レビュー完了、指摘事項なし

---

## 📅 スケジュール概要

| Phase | タスク | 所要時間 | 累積時間 |
|-------|--------|----------|----------|
| **2.1** | 基盤準備・インターフェース定義 | 2-3日 | 2-3日 |
| **2.2** | ユーティリティ・パフォーマンストラッカー | 2日 | 4-5日 |
| **2.3** | エラーハンドラー・言語最適化 | 3日 | 7-8日 |
| **2.4** | モデルマネージャー | 3-4日 | 10-12日 |
| **2.5** | 画像プロセッサー | 4-5日 | 14-17日 |
| **2.6** | エンジン初期化 | 3日 | 17-20日 |
| **2.7** | OCR実行エグゼキューター | 4-5日 | 21-25日 |
| **2.8** | 結果コンバーター | 3-4日 | 24-29日 |
| **2.9** | PaddleOcrEngineリファクタリング | 3-4日 | 27-33日 |
| **2.10** | 統合テスト・ドキュメント整備 | 3日 | 30-36日 |

**合計所要時間**: 約30-36日（約1.5-2ヶ月）

---

## 🔄 継続的改善

### フィードバックループ
- 各Phase完了時点でGeminiレビュー実施
- 指摘事項を次Phaseに反映
- 定期的な設計見直し

### 品質メトリクス追跡
- コード行数削減率
- 循環的複雑度
- テストカバレッジ
- ビルド時間
- テスト実行時間

---

## 📞 質問・相談

リファクタリング実施中の質問・相談は以下の方法で：

1. **技術的質問**: Gemini専門レビューを活用
2. **設計判断**: Architecture-Guardianエージェントに相談
3. **パフォーマンス問題**: 計測データを添えて相談

---

## 🔍 Gemini専門レビュー結果 (2025-10-04)

### 総評: ✅ 実装推奨

**「決定的な問題点はなく、この計画に沿って実装を進めることを強く推奨します。非常にリスクが低減された、現実的かつ効果的なアプローチです。」**

### レビュー結果詳細

#### 1. アーキテクチャ設計の妥当性 ✅
- **クラス分割**: 適切。単一責任の原則（SRP）に完全に基づいている
- **責任範囲**: 明確に分離されている
- **インターフェース設計**: 適切。DIPを遵守し、DIとモック化を容易にしている
- **Clean Architecture準拠**: はい、完全に準拠している

#### 2. 実装計画の実現可能性 ✅
- **段階的実装計画**: 現実的かつ非常に優れている
- **所要時間見積もり**: 妥当（30-36日は現実的）
- **フェーズの順序**: 最適。依存関係が論理的に解決される順序

#### 3. リスク評価の妥当性 ✅
- **リスク評価**: 的確
- **リスク軽減策**: すべて適切

#### 4. テスト戦略の適切性 ✅
- **方針**: 理想的
- **カバレッジ目標**: 80%以上は現実的かつ十分
- **モック戦略**: インターフェースベースで容易

#### 5. パフォーマンス懸念事項 ✅
- **サービス分割のオーバーヘッド**: 許容範囲内（ナノ秒オーダー vs 数十〜数百ミリ秒）
- **メモリ使用量**: 大幅増加の可能性は低い

### 改善提案

#### 1. DIライフサイクルの再検討 ⚠️
**現状**: 全サービスを`Singleton`として計画
**提案**: `PaddleOcrAll`のような重量級オブジェクトは`Scoped`や`Transient`が適切かもしれない
**対応**: Phase 2.1実装時に各サービスの最適なライフタイムを再検討

#### 2. Feature Flagの導入検討 💡
**提案**: 新旧処理パスを一時的に共存させ、設定で切り替え可能にする
**利点**: 本番環境で問題発生時に即座に安定版に切り戻し可能
**対応**: Phase 2.9（PaddleOcrEngineリファクタリング）で検討

#### 3. 設定アクセスの一元化検討 💡
**現状**: 複数のサービスが`IUnifiedSettingsService`に依存
**提案**: 設定専用のコンフィギュレーションクラスを各サービスが受け取る設計
**対応**: Phase 2.1実装時に検討

### 追加リスク

#### 1. DIライフサイクル管理 ⚠️
`PooledOcrService`との連携を考慮し、各サービスの最適なライフタイム管理が必要

#### 2. ラッパークラスへの深い依存 ⚠️
既存ラッパークラスが`PaddleOcrEngine`のpublicでないメンバーに依存している可能性
→ Phase 2.1でラッパークラスの依存関係を詳細調査

### 結論

✅ **この計画で実装を進めて良い**
✅ **修正すべき重大な問題はない**
✅ **代替アプローチは不要**

「このリファクタリング計画は、技術的な負債を解消し、将来の拡張性と保守性を劇的に向上させるための優れたロードマップです。計画の質が非常に高いため、自信を持ってこのまま実行に移してください。」

---

**このリファクタリング計画書は、PaddleOcrEngine.csの保守性・テスト容易性を大幅に向上させ、Clean Architecture原則に完全準拠した設計を実現するための詳細なロードマップです。Gemini専門レビューにより技術的な健全性が確認されており、段階的な実装により、既存機能を破壊することなく、安全かつ確実にリファクタリングを完了させることができます。**
