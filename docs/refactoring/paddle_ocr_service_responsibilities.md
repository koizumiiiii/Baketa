# PaddleOCR Services - 責任範囲詳細

## 概要

Phase 2.9リファクタリングで作成された6サービスの詳細責任範囲、公開メソッド、使用例を記載します。

---

## 1. IPaddleOcrModelManager

**責任**: モデル管理、モデルロード、モデル選択、言語管理

**実装クラス**: `PaddleOcrModelManager`
**ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Services/PaddleOcrModelManager.cs`
**行数**: 約360行

### 公開メソッド

#### PrepareModelsAsync
```csharp
Task<FullOcrModel?> PrepareModelsAsync(string language, CancellationToken cancellationToken)
```
**責任**: 指定言語のモデル準備（LocalFullModelsまたはPP-OCRv5）
**返り値**: FullOcrModel（成功）またはnull（テスト環境/失敗時）

**使用例**:
```csharp
var modelManager = serviceProvider.GetRequiredService<IPaddleOcrModelManager>();
var model = await modelManager.PrepareModelsAsync("eng", CancellationToken.None);
if (model != null)
{
    // モデル使用可能
}
```

#### GetDefaultModelForLanguage
```csharp
FullOcrModel? GetDefaultModelForLanguage(string language)
```
**責任**: 言語別デフォルトモデル取得
**言語マッピング**:
- `jpn`/`ja` → LocalFullModels.JapanV4
- `eng`/`en` → LocalFullModels.EnglishV4
- `chs`/`zh`/`chi` → LocalFullModels.ChineseV4

#### GetAvailableLanguages (Phase 2.9.6追加)
```csharp
IReadOnlyList<string> GetAvailableLanguages()
```
**責任**: PP-OCRv5がサポートする言語リスト取得
**返り値**: `["eng", "jpn", "chi_sim"]`

#### GetAvailableModels (Phase 2.9.6追加)
```csharp
IReadOnlyList<string> GetAvailableModels()
```
**責任**: 利用可能なモデル種別リスト取得
**返り値**: `["standard", "ppocrv5"]`

#### IsLanguageAvailableAsync (Phase 2.9.6追加)
```csharp
Task<bool> IsLanguageAvailableAsync(string languageCode, CancellationToken cancellationToken)
```
**責任**: 指定言語のモデル可用性確認
**使用例**:
```csharp
if (await modelManager.IsLanguageAvailableAsync("jpn", ct))
{
    // 日本語モデルが利用可能
}
```

#### DetectIfV5Model
```csharp
bool DetectIfV5Model(FullOcrModel model)
```
**責任**: V5モデルかどうかの検出
**返り値**: 常にtrue（V5統一により）

---

## 2. IPaddleOcrImageProcessor

**責任**: 画像変換、前処理、ROIクロッピング、リサイズ

**実装クラス**: `PaddleOcrImageProcessor`
**ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Services/PaddleOcrImageProcessor.cs`
**行数**: 約300行

### 公開メソッド

#### ConvertToMatAsync
```csharp
Task<Mat> ConvertToMatAsync(IImage image, CancellationToken cancellationToken)
```
**責任**: IImage → OpenCvSharp.Mat変換
**使用例**:
```csharp
var imageProcessor = serviceProvider.GetRequiredService<IPaddleOcrImageProcessor>();
var mat = await imageProcessor.ConvertToMatAsync(image, CancellationToken.None);
// mat は OpenCV形式の画像データ
```

#### ApplyPreprocessing
```csharp
Task<PreprocessingResult> ApplyPreprocessing(
    Mat sourceMat,
    Rectangle? regionOfInterest,
    CancellationToken cancellationToken)
```
**責任**: 前処理適用（ROIクロッピング、リサイズ、スケール計算）
**返り値**: `PreprocessingResult`（処理済みMat、スケールファクター）

**使用例**:
```csharp
var roi = new Rectangle(10, 10, 500, 300);
var result = await imageProcessor.ApplyPreprocessing(mat, roi, ct);
// result.ProcessedMat - 処理済み画像
// result.ScaleFactor - 元画像からのスケール比
```

### PreprocessingResult構造
```csharp
public sealed class PreprocessingResult
{
    public Mat ProcessedMat { get; init; }
    public double ScaleFactor { get; init; }
    public Rectangle? ActualRoi { get; init; }
}
```

---

## 3. IPaddleOcrResultConverter

**責任**: PaddleOCR結果変換、座標復元、テキスト結合

**実装クラス**: `PaddleOcrResultConverter`
**ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Services/PaddleOcrResultConverter.cs`
**行数**: 約400行

### 公開メソッド

#### ConvertToTextRegions
```csharp
IReadOnlyList<OcrTextRegion> ConvertToTextRegions(
    PaddleOcrResult[] paddleResults,
    double scaleFactor,
    Rectangle? roi)
```
**責任**: PaddleOcrResult[] → OcrTextRegion[]変換、座標復元
**使用例**:
```csharp
var resultConverter = serviceProvider.GetRequiredService<IPaddleOcrResultConverter>();
var textRegions = resultConverter.ConvertToTextRegions(
    paddleResults,
    scaleFactor: 0.5, // 画像が50%縮小されていた場合
    roi: new Rectangle(100, 100, 500, 500)
);
// textRegions - 元画像座標系に復元されたOcrTextRegion[]
```

#### ConvertDetectionOnlyResult
```csharp
IReadOnlyList<OcrTextRegion> ConvertDetectionOnlyResult(PaddleOcrResult[] paddleResults)
```
**責任**: 検出専用結果の変換（テキスト認識なし、座標のみ）

#### CreateEmptyResult
```csharp
OcrResults CreateEmptyResult(IImage image, Rectangle? roi, TimeSpan processingTime)
```
**責任**: 空のOcrResults作成（エラー時やテキストなし時）

---

## 4. IPaddleOcrExecutor

**責任**: 実際のPaddleOCR実行、タイムアウト管理、リトライ処理

**実装クラス**: `PaddleOcrExecutor`
**ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Services/PaddleOcrExecutor.cs`
**行数**: 約350行

### 公開メソッド

#### ExecuteOcrAsync
```csharp
Task<PaddleOcrResult> ExecuteOcrAsync(
    Mat processedMat,
    IProgress<OcrProgress>? progress,
    CancellationToken cancellationToken)
```
**責任**: 認識付きOCR実行、進捗レポート、タイムアウト管理
**使用例**:
```csharp
var executor = serviceProvider.GetRequiredService<IPaddleOcrExecutor>();
var progress = new Progress<OcrProgress>(p =>
{
    Console.WriteLine($"OCR進捗: {p.Message}");
});

var paddleResult = await executor.ExecuteOcrAsync(mat, progress, ct);
// paddleResult.Regions - 検出されたテキスト領域と認識結果
```

#### ExecuteDetectionOnlyAsync
```csharp
Task<PaddleOcrResult> ExecuteDetectionOnlyAsync(Mat processedMat, CancellationToken cancellationToken)
```
**責任**: 検出専用OCR実行（テキスト認識スキップ、高速）

#### CancelCurrentOcrTimeout (Phase 2.9.6委譲)
```csharp
void CancelCurrentOcrTimeout()
```
**責任**: 現在のOCRタイムアウトをキャンセル

---

## 5. IPaddleOcrPerformanceTracker

**責任**: パフォーマンス統計収集、タイムアウト計算、失敗カウンター管理

**実装クラス**: `PaddleOcrPerformanceTracker`
**ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Services/PaddleOcrPerformanceTracker.cs`
**行数**: 約200行

### 公開メソッド

#### UpdatePerformanceStats
```csharp
void UpdatePerformanceStats(double processingTimeMs, bool success)
```
**責任**: パフォーマンス統計更新（処理時間、成功/失敗）
**使用例**:
```csharp
var performanceTracker = serviceProvider.GetRequiredService<IPaddleOcrPerformanceTracker>();
var stopwatch = Stopwatch.StartNew();
try
{
    var result = await ExecuteOcr();
    stopwatch.Stop();
    performanceTracker.UpdatePerformanceStats(stopwatch.Elapsed.TotalMilliseconds, success: true);
}
catch
{
    stopwatch.Stop();
    performanceTracker.UpdatePerformanceStats(stopwatch.Elapsed.TotalMilliseconds, success: false);
}
```

#### GetPerformanceStats (Phase 2.9.6委譲)
```csharp
OcrPerformanceStats GetPerformanceStats()
```
**責任**: パフォーマンス統計取得
**返り値**: `OcrPerformanceStats`（平均処理時間、成功率、失敗回数など）

#### CalculateTimeout
```csharp
int CalculateTimeout(Mat mat)
```
**責任**: 画像サイズに基づくタイムアウト計算

#### GetAdaptiveTimeout
```csharp
int GetAdaptiveTimeout(int baseTimeout)
```
**責任**: 過去の失敗率に基づくアダプティブタイムアウト計算

#### ResetFailureCounter (Phase 2.9.6委譲)
```csharp
void ResetFailureCounter()
```
**責任**: 連続失敗カウンターのリセット

#### GetConsecutiveFailureCount (Phase 2.9.6委譲)
```csharp
int GetConsecutiveFailureCount()
```
**責任**: 連続失敗回数取得

---

## 6. IPaddleOcrErrorHandler

**責任**: エラー診断、エラーメッセージ生成、解決策提案

**実装クラス**: `PaddleOcrErrorHandler`
**ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Services/PaddleOcrErrorHandler.cs`
**行数**: 約150行

### 公開メソッド

#### HandleError
```csharp
void HandleError(Exception ex, string context)
```
**責任**: エラー処理、診断イベント発行
**使用例**:
```csharp
var errorHandler = serviceProvider.GetRequiredService<IPaddleOcrErrorHandler>();
try
{
    var result = await ExecuteOcr();
}
catch (Exception ex)
{
    errorHandler.HandleError(ex, "RecognizeAsync");
    // 診断イベントが発行され、ログに詳細エラー情報が記録される
}
```

---

## サービス使用例（統合）

### RecognizeAsync完全フロー

```csharp
public async Task<OcrResults> RecognizeAsync(
    IImage image,
    Rectangle? roi,
    IProgress<OcrProgress>? progress,
    CancellationToken cancellationToken)
{
    var stopwatch = Stopwatch.StartNew();

    try
    {
        // 1. 画像変換
        var mat = await _imageProcessor.ConvertToMatAsync(image, cancellationToken);

        // 2. 前処理適用
        var preprocessingResult = await _imageProcessor.ApplyPreprocessing(mat, roi, cancellationToken);

        // 3. OCR実行
        var paddleResult = await _executor.ExecuteOcrAsync(
            preprocessingResult.ProcessedMat,
            progress,
            cancellationToken);

        // 4. 結果変換
        var textRegions = _resultConverter.ConvertToTextRegions(
            new[] { paddleResult },
            preprocessingResult.ScaleFactor,
            roi);

        stopwatch.Stop();

        // 5. パフォーマンス統計更新
        _performanceTracker.UpdatePerformanceStats(stopwatch.Elapsed.TotalMilliseconds, success: true);

        return new OcrResults
        {
            TextRegions = textRegions,
            RegionOfInterest = roi,
            ProcessingTime = stopwatch.Elapsed
        };
    }
    catch (Exception ex)
    {
        stopwatch.Stop();

        // エラー処理
        _errorHandler.HandleError(ex, nameof(RecognizeAsync));
        _performanceTracker.UpdatePerformanceStats(stopwatch.Elapsed.TotalMilliseconds, success: false);

        // 空結果返却
        return _resultConverter.CreateEmptyResult(image, roi, stopwatch.Elapsed);
    }
}
```

## 依存関係注入（DI）

### サービス登録例（PaddleOcrModule.cs）

```csharp
public override void Load(IServiceCollection services, ServiceModuleContext context)
{
    // Phase 2.9 新規サービス登録
    services.AddSingleton<IPaddleOcrModelManager, PaddleOcrModelManager>();
    services.AddSingleton<IPaddleOcrImageProcessor, PaddleOcrImageProcessor>();
    services.AddSingleton<IPaddleOcrResultConverter, PaddleOcrResultConverter>();
    services.AddSingleton<IPaddleOcrExecutor, PaddleOcrExecutor>();
    services.AddSingleton<IPaddleOcrPerformanceTracker, PaddleOcrPerformanceTracker>();
    services.AddSingleton<IPaddleOcrErrorHandler, PaddleOcrErrorHandler>();

    // Facade
    services.AddSingleton<IOcrEngine, PaddleOcrEngine>();
}
```

### コンストラクターインジェクション例

```csharp
public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    private readonly IPaddleOcrImageProcessor _imageProcessor;
    private readonly IPaddleOcrResultConverter _resultConverter;
    private readonly IPaddleOcrExecutor _executor;
    private readonly IPaddleOcrModelManager _modelManager;
    private readonly IPaddleOcrPerformanceTracker _performanceTracker;
    private readonly IPaddleOcrErrorHandler _errorHandler;

    public PaddleOcrEngine(
        IPaddleOcrImageProcessor imageProcessor,
        IPaddleOcrResultConverter resultConverter,
        IPaddleOcrExecutor executor,
        IPaddleOcrModelManager modelManager,
        IPaddleOcrPerformanceTracker performanceTracker,
        IPaddleOcrErrorHandler errorHandler)
    {
        _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
        _resultConverter = resultConverter ?? throw new ArgumentNullException(nameof(resultConverter));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _performanceTracker = performanceTracker ?? throw new ArgumentNullException(nameof(performanceTracker));
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
    }
}
```

## テスタビリティ

### モック例（単体テスト）

```csharp
[Fact]
public async Task RecognizeAsync_WithROI_AppliesCorrectScale()
{
    // Arrange
    var mockImageProcessor = new Mock<IPaddleOcrImageProcessor>();
    var mockResultConverter = new Mock<IPaddleOcrResultConverter>();
    var mockExecutor = new Mock<IPaddleOcrExecutor>();
    // ... その他のモック

    mockImageProcessor
        .Setup(x => x.ApplyPreprocessing(It.IsAny<Mat>(), It.IsAny<Rectangle?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PreprocessingResult
        {
            ProcessedMat = new Mat(),
            ScaleFactor = 0.5
        });

    var engine = new PaddleOcrEngine(
        mockImageProcessor.Object,
        mockResultConverter.Object,
        mockExecutor.Object,
        // ... その他のモック
    );

    // Act
    var result = await engine.RecognizeAsync(mockImage, roi, null, CancellationToken.None);

    // Assert
    mockResultConverter.Verify(x => x.ConvertToTextRegions(
        It.IsAny<PaddleOcrResult[]>(),
        0.5, // スケールファクター検証
        It.IsAny<Rectangle?>()), Times.Once);
}
```

## 関連ドキュメント

- [Facadeアーキテクチャ図](./paddle_ocr_facade_architecture.md)
- [リファクタリング計画全体](./paddle_ocr_refactoring_plan.md)
- [テスト戦略ガイド](./paddle_ocr_testing_guide.md)

## Phase 2.10完了確認

**ステータス**: ✅ **完全達成** (2025-10-05)

### 6サービスの責任分離達成

| サービス | 責任範囲 | 行数 | テスト | 状態 |
|---------|---------|------|--------|------|
| **PaddleOcrModelManager** | モデル管理・言語管理 | 360行 | ✅ 単体 | 完了 |
| **PaddleOcrImageProcessor** | 画像変換・前処理 | 300行 | ✅ 統合 | 完了 |
| **PaddleOcrResultConverter** | 結果変換・座標復元 | 400行 | ✅ 単体 | 完了 |
| **PaddleOcrExecutor** | OCR実行・タイムアウト | 350行 | ✅ 統合 | 完了 |
| **PaddleOcrPerformanceTracker** | 統計収集・タイムアウト計算 | 200行 | ✅ 統合 | 完了 |
| **PaddleOcrErrorHandler** | エラー診断・解決策提案 | 150行 | ✅ 統合 | 完了 |

**合計**: 1,760行の専門サービス（PaddleOcrEngine Facade: 4,547行から分離）

### Clean Architecture準拠確認
- ✅ **疎結合**: 6サービス相互依存なし
- ✅ **テスタビリティ**: 全サービスモック可能
- ✅ **単一責任原則**: 各サービス明確な責任範囲
- ✅ **依存性注入**: コンストラクターインジェクション統一

---

## 更新履歴

- **2025-10-05**: ✅ **Phase 2.10完全達成** - 6サービス詳細ドキュメント完成
- **2025-10-05**: Phase 2.10完了、サービス責任範囲詳細作成
