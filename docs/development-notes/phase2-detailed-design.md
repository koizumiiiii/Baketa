# Phase 2 詳細設計書 - OCR精度向上と座標ベース翻訳システム

## 📅 作成日: 2025-07-25
## 🎯 目標: OCRバッチ処理最適化 + 座標ベース翻訳表示基盤

---

## 🧠 UltraThink Analysis

### 根本原因分析
**問題の本質**: 現在のシステムはテキスト処理と座標情報が分離されており、OCR→翻訳→表示の流れで座標情報が失われている

**アーキテクチャ上の制約**: 
- OCR結果のデータ構造が座標情報保持に最適化されていない
- 翻訳パイプラインが座標情報を破棄している  
- オーバーレイシステムが単一ウィンドウ前提で設計されている

### 影響分析
**高影響**: 翻訳パイプライン、オーバーレイシステム、データ構造
**中影響**: OCRエンジン、UI表示基盤、パフォーマンス
**低影響**: 設定システム、翻訳エンジン

---

## 🎯 Phase 2 実装目標

### 主要目標
1. **OCRバッチ処理最適化** - 前処理パイプライン統合による精度向上
2. **座標情報保持システム** - テキスト塊と位置情報の関連付け
3. **複数ウィンドウ表示基盤** - 翻訳結果の座標ベース表示準備

### 性能目標
- OCR処理時間: 1.2s → 0.8s以下
- 精度向上: 前処理パイプライン適用による10-20%改善
- メモリ効率: GPU活用による効率化

---

## 🏗️ システム設計

### 1. データ構造拡張

#### 1.1 座標付きテキスト結果 (新規)
```csharp
// Baketa.Core/Abstractions/OCR/Results/PositionedTextResult.cs
public sealed class PositionedTextResult
{
    public string Text { get; init; } = string.Empty;
    public Rectangle BoundingBox { get; init; }
    public float Confidence { get; init; }
    public int ChunkId { get; init; }
    public TimeSpan ProcessingTime { get; init; }
    
    // 座標ログ用
    public string ToLogString() => 
        $"Text: '{Text}' | Bounds: ({BoundingBox.X},{BoundingBox.Y},{BoundingBox.Width},{BoundingBox.Height}) | Confidence: {Confidence:F2} | ChunkId: {ChunkId}";
}
```

#### 1.2 テキスト塊管理 (新規)
```csharp
// Baketa.Core/Abstractions/Translation/TextChunk.cs
public sealed class TextChunk
{
    public int ChunkId { get; init; }
    public IReadOnlyList<PositionedTextResult> TextResults { get; init; } = [];
    public Rectangle CombinedBounds { get; init; }
    public string CombinedText { get; init; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public IntPtr SourceWindowHandle { get; init; }
}
```

### 2. OCRエンジン拡張

#### 2.1 PaddleOCRエンジン修正
```csharp
// Baketa.Infrastructure/OCR/PaddleOCR/Engine/PaddleOcrEngine.cs
// 既存メソッドに座標ログ出力を追加

public async Task<IReadOnlyList<PositionedTextResult>> ProcessWithCoordinatesAsync(
    IAdvancedImage image, CancellationToken cancellationToken = default)
{
    var results = new List<PositionedTextResult>();
    
    // 既存のOCR処理
    var ocrResults = await base.ProcessAsync(image, cancellationToken);
    
    // 座標情報付きの結果に変換
    foreach (var result in ocrResults)
    {
        var positioned = new PositionedTextResult
        {
            Text = result.Text,
            BoundingBox = result.BoundingBox,
            Confidence = result.Confidence,
            ChunkId = GenerateChunkId(result.BoundingBox),
            ProcessingTime = result.ProcessingTime
        };
        
        // 📋 座標ログ出力 (ユーザー要求)
        _logger.LogInformation("📍 OCR結果: {PositionedResult}", positioned.ToLogString());
        
        results.Add(positioned);
    }
    
    return results;
}
```

#### 2.2 適応的前処理パイプライン統合
```csharp
// Baketa.Infrastructure/OCR/PaddleOCR/Engine/EnhancedPaddleOcrEngine.cs (新規)
public sealed class EnhancedPaddleOcrEngine : PaddleOcrEngine
{
    private readonly IImagePipeline _preprocessingPipeline;
    private readonly IImageQualityAnalyzer _qualityAnalyzer;
    
    public override async Task<IReadOnlyList<PositionedTextResult>> ProcessWithCoordinatesAsync(
        IAdvancedImage image, CancellationToken cancellationToken = default)
    {
        // 1. 画像品質分析
        var quality = await _qualityAnalyzer.AnalyzeAsync(image);
        _logger.LogInformation("📊 画像品質分析: Score={QualityScore}, Brightness={Brightness}, Contrast={Contrast}", 
            quality.Score, quality.Brightness, quality.Contrast);
        
        // 2. 適応的前処理パイプライン適用
        var enhancedImage = await _preprocessingPipeline.ProcessAsync(image, quality);
        _logger.LogInformation("🔧 前処理完了: Applied filters={FilterCount}", 
            _preprocessingPipeline.AppliedFilters.Count);
        
        // 3. OCR実行
        return await base.ProcessWithCoordinatesAsync(enhancedImage, cancellationToken);
    }
}
```

### 3. バッチ処理最適化システム

#### 3.1 バッチOCRプロセッサ (新規)
```csharp
// Baketa.Application/Services/OCR/BatchOcrProcessor.cs (新規)
public sealed class BatchOcrProcessor : IBatchOcrProcessor
{
    private readonly IEnhancedPaddleOcrEngine _ocrEngine;
    private readonly ITextChunkingService _chunkingService;
    private readonly IParallelProcessingManager _parallelManager;
    
    public async Task<IReadOnlyList<TextChunk>> ProcessBatchAsync(
        IAdvancedImage image, IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // 1. 並列可能な領域に分割
        var regions = await _chunkingService.DetectTextRegionsAsync(image);
        _logger.LogInformation("🔍 テキスト領域検出: {RegionCount}個の領域を検出", regions.Count);
        
        // 2. 並列OCR処理
        var tasks = regions.Select(region => ProcessRegionAsync(image, region, cancellationToken));
        var results = await Task.WhenAll(tasks);
        
        // 3. 結果をチャンクに整理
        var chunks = await _chunkingService.GroupIntoChunksAsync(results.SelectMany(r => r), windowHandle);
        
        stopwatch.Stop();
        _logger.LogInformation("⚡ バッチOCR完了: {ChunkCount}個のチャンク、処理時間={ProcessingTime}ms", 
            chunks.Count, stopwatch.ElapsedMilliseconds);
        
        return chunks;
    }
    
    private async Task<IReadOnlyList<PositionedTextResult>> ProcessRegionAsync(
        IAdvancedImage image, Rectangle region, CancellationToken cancellationToken)
    {
        // ROIベース部分処理
        using var regionImage = image.ExtractRegion(region);
        var results = await _ocrEngine.ProcessWithCoordinatesAsync(regionImage, cancellationToken);
        
        // 座標を元画像基準に変換
        return results.Select(r => r with { 
            BoundingBox = new Rectangle(
                r.BoundingBox.X + region.X,
                r.BoundingBox.Y + region.Y,
                r.BoundingBox.Width,
                r.BoundingBox.Height
            )
        }).ToList();
    }
}
```

### 4. 翻訳システム拡張

#### 4.1 翻訳オーケストレーション修正
```csharp
// Baketa.Application/Services/Translation/TranslationOrchestrationService.cs
// 既存メソッドを座標対応に拡張

public async Task<IReadOnlyList<TextChunk>> TranslateWithCoordinatesAsync(
    IntPtr windowHandle, CancellationToken cancellationToken = default)
{
    try
    {
        // 1. バッチOCR実行
        var image = await _captureService.CaptureAsync(windowHandle);
        var chunks = await _batchOcrProcessor.ProcessBatchAsync(image, windowHandle, cancellationToken);
        
        // 2. チャンクごとに翻訳
        var translationTasks = chunks.Select(chunk => TranslateChunkAsync(chunk, cancellationToken));
        await Task.WhenAll(translationTasks);
        
        // 3. 座標ログ出力 (ユーザー要求)
        foreach (var chunk in chunks)
        {
            _logger.LogInformation("🌐 翻訳完了: ChunkId={ChunkId} | 原文='{Original}' | 訳文='{Translated}' | 座標=({X},{Y},{W},{H})",
                chunk.ChunkId, chunk.CombinedText, chunk.TranslatedText, 
                chunk.CombinedBounds.X, chunk.CombinedBounds.Y, 
                chunk.CombinedBounds.Width, chunk.CombinedBounds.Height);
        }
        
        return chunks;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ 座標ベース翻訳処理中にエラー");
        throw;
    }
}

private async Task TranslateChunkAsync(TextChunk chunk, CancellationToken cancellationToken)
{
    var request = new TranslationRequest
    {
        SourceText = chunk.CombinedText,
        SourceLanguage = "ja", // TODO: 自動検出
        TargetLanguage = "en"
    };
    
    var result = await _translationEngine.TranslateAsync(request, cancellationToken);
    chunk.TranslatedText = result.TranslatedText;
}
```

### 5. オーバーレイ基盤準備

#### 5.1 複数ウィンドウオーバーレイマネージャー (新規)
```csharp
// Baketa.UI/Services/MultiWindowOverlayManager.cs (新規)
public sealed class MultiWindowOverlayManager : IMultiWindowOverlayManager
{
    private readonly Dictionary<int, TranslationOverlayWindow> _overlayWindows = [];
    private readonly ILogger<MultiWindowOverlayManager> _logger;
    
    public async Task DisplayTranslationResultsAsync(IReadOnlyList<TextChunk> chunks)
    {
        // 既存のオーバーレイをクリア
        ClearExistingOverlays();
        
        // チャンクごとにオーバーレイウィンドウを作成
        foreach (var chunk in chunks)
        {
            var overlayWindow = CreateOverlayWindow(chunk);
            _overlayWindows[chunk.ChunkId] = overlayWindow;
            
            _logger.LogInformation("📺 オーバーレイ表示: ChunkId={ChunkId} | Position=({X},{Y}) | Text='{Text}'",
                chunk.ChunkId, chunk.CombinedBounds.X, chunk.CombinedBounds.Y, chunk.TranslatedText);
            
            await overlayWindow.ShowAtPositionAsync(chunk.CombinedBounds);
        }
    }
    
    private TranslationOverlayWindow CreateOverlayWindow(TextChunk chunk)
    {
        return new TranslationOverlayWindow
        {
            ChunkId = chunk.ChunkId,
            OriginalText = chunk.CombinedText,
            TranslatedText = chunk.TranslatedText,
            TargetBounds = chunk.CombinedBounds,
            SourceWindow = chunk.SourceWindowHandle
        };
    }
}
```

#### 5.2 位置指定オーバーレイウィンドウ (新規)
```csharp
// Baketa.UI/Views/TranslationOverlayWindow.cs (新規)
public sealed class TranslationOverlayWindow : Window
{
    public int ChunkId { get; init; }
    public string OriginalText { get; init; } = string.Empty;
    public string TranslatedText { get; init; } = string.Empty;
    public Rectangle TargetBounds { get; init; }
    public IntPtr SourceWindow { get; init; }
    
    public async Task ShowAtPositionAsync(Rectangle bounds)
    {
        // ウィンドウサイズを翻訳テキストに合わせて調整
        var textSize = MeasureTextSize(TranslatedText);
        
        // 対象テキストの近くに配置（オフセット調整）
        var position = CalculateOptimalPosition(bounds, textSize);
        
        Position = new PixelPoint(position.X, position.Y);
        Width = textSize.Width;
        Height = textSize.Height;
        
        // 透明度・スタイル設定
        Background = new SolidColorBrush(Colors.Black, 0.8);
        
        Show();
        await Task.CompletedTask;
    }
    
    private Point CalculateOptimalPosition(Rectangle targetBounds, Size textSize)
    {
        // テキスト領域の下側に表示（画面外の場合は上側）
        var x = targetBounds.X;
        var y = targetBounds.Y + targetBounds.Height + 5;
        
        // 画面境界チェック・調整
        // TODO: マルチモニター対応
        
        return new Point(x, y);
    }
}
```

### 6. GPU活用・パフォーマンス最適化

#### 6.1 GPU加速前処理パイプライン
```csharp
// Baketa.Infrastructure/Imaging/GPU/GpuAcceleratedPipeline.cs (新規)
public sealed class GpuAcceleratedPipeline : IImagePipeline
{
    private readonly IWindowsOpenCvWrapper _openCv;
    private readonly IGPUEnvironmentDetector _gpuDetector;
    
    public async Task<IAdvancedImage> ProcessAsync(IAdvancedImage image, ImageQualityMetrics quality)
    {
        if (!_gpuDetector.GetEnvironment().HasDedicatedGPU)
        {
            // CPUフォールバック
            return await _cpuPipeline.ProcessAsync(image, quality);
        }
        
        // GPU加速処理
        using var mat = image.ToMat();
        
        // 並列GPU処理
        var tasks = new[]
        {
            Task.Run(() => _openCv.GpuGaussianBlur(mat, quality.BlurKernel)),
            Task.Run(() => _openCv.GpuContrastEnhancement(mat, quality.ContrastLevel)),
            Task.Run(() => _openCv.GpuNoiseReduction(mat, quality.NoiseLevel))
        };
        
        await Task.WhenAll(tasks);
        
        return image.FromMat(mat);
    }
}
```

---

## 📋 実装計画

### Phase 2-A: データ構造・基盤実装 (Week 1)
1. **PositionedTextResult/TextChunk定義**
2. **IBatchOcrProcessor/IMultiWindowOverlayManagerインターフェース**
3. **座標ログ機能の基本実装**

### Phase 2-B: OCR最適化実装 (Week 2)
1. **EnhancedPaddleOcrEngine実装**
2. **BatchOcrProcessor実装**
3. **GPU加速前処理パイプライン**

### Phase 2-C: 翻訳システム統合 (Week 3)
1. **TranslationOrchestrationService拡張**
2. **座標情報保持機能の実装**
3. **エラーハンドリング強化**

### Phase 2-D: UI基盤準備 (Week 4)
1. **MultiWindowOverlayManager実装**
2. **TranslationOverlayWindow基本実装**
3. **座標ベース表示の基盤確立**

---

## 🧪 テスト戦略

### 単体テスト
- PositionedTextResult/TextChunk データ構造テスト
- BatchOcrProcessor テスト
- 座標変換ロジック テスト

### 統合テスト
- OCR→翻訳→表示の座標情報伝播テスト
- 複数ウィンドウでの表示テスト
- パフォーマンス回帰テスト

### 性能テスト
- バッチ処理 vs シーケンシャル処理の比較
- GPU加速 vs CPU処理の比較
- メモリ使用量・処理時間測定

---

## 🎯 成功基準

### 機能面
- ✅ テキスト座標のログ出力機能
- ✅ 複数ウィンドウでの翻訳表示基盤
- ✅ OCR精度の10-20%向上

### 性能面
- ✅ OCR処理時間: 1.2s → 0.8s以下
- ✅ メモリ効率の向上
- ✅ GPU活用による高速化

### 品質面
- ✅ 既存機能の回帰なし
- ✅ 0エラー・0警告の維持
- ✅ ログ出力の充実

---

**作成者**: Claude  
**作成日**: 2025-07-25  
**ステータス**: 設計完了、実装準備完了