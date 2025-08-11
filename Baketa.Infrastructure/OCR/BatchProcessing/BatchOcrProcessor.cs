using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.Abstractions.Performance;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Performance;
using Baketa.Core.Logging;
using Baketa.Infrastructure.OCR.PostProcessing;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Baketa.Infrastructure.OCR.BatchProcessing;

/// <summary>
/// 文字体系の種類
/// </summary>
public enum WritingSystem
{
    /// <summary>
    /// 不明
    /// </summary>
    Unknown,
    
    /// <summary>
    /// 表意文字（漢字、漢字かな混じり文など）
    /// </summary>
    Logographic,
    
    /// <summary>
    /// アルファベット（ラテン文字、キリル文字など）
    /// </summary>
    Alphabetic,
    
    /// <summary>
    /// 音節文字（ハングルなど）
    /// </summary>
    Syllabic,
    
    /// <summary>
    /// 子音文字（アラビア文字など）
    /// </summary>
    Abjad,
    
    /// <summary>
    /// アブギダ（デーヴァナーガリーなど）
    /// </summary>
    Abugida
}

/// <summary>
/// 言語情報
/// </summary>
public readonly record struct LanguageInfo
{
    public string Code { get; init; }
    public string Name { get; init; }
    public WritingSystem WritingSystem { get; init; }
    public bool RequiresSpaceSeparation { get; init; }
    public bool HasParticles { get; init; }
    public bool IsRightToLeft { get; init; }
    
    public static readonly LanguageInfo Japanese = new()
    {
        Code = "ja",
        Name = "Japanese",
        WritingSystem = WritingSystem.Logographic,
        RequiresSpaceSeparation = false,
        HasParticles = true,
        IsRightToLeft = false
    };
    
    public static readonly LanguageInfo English = new()
    {
        Code = "en",
        Name = "English",
        WritingSystem = WritingSystem.Alphabetic,
        RequiresSpaceSeparation = true,
        HasParticles = false,
        IsRightToLeft = false
    };
    
    public static readonly LanguageInfo Chinese = new()
    {
        Code = "zh",
        Name = "Chinese",
        WritingSystem = WritingSystem.Logographic,
        RequiresSpaceSeparation = false,
        HasParticles = false,
        IsRightToLeft = false
    };
    
    public static readonly LanguageInfo Korean = new()
    {
        Code = "ko",
        Name = "Korean",
        WritingSystem = WritingSystem.Syllabic,
        RequiresSpaceSeparation = true,
        HasParticles = true,
        IsRightToLeft = false
    };
    
    public static readonly LanguageInfo German = new()
    {
        Code = "de",
        Name = "German",
        WritingSystem = WritingSystem.Alphabetic,
        RequiresSpaceSeparation = true,
        HasParticles = false,
        IsRightToLeft = false
    };
    
    public static readonly LanguageInfo French = new()
    {
        Code = "fr",
        Name = "French",
        WritingSystem = WritingSystem.Alphabetic,
        RequiresSpaceSeparation = true,
        HasParticles = false,
        IsRightToLeft = false
    };
    
    public static readonly LanguageInfo Spanish = new()
    {
        Code = "es",
        Name = "Spanish",
        WritingSystem = WritingSystem.Alphabetic,
        RequiresSpaceSeparation = true,
        HasParticles = false,
        IsRightToLeft = false
    };
    
    public static readonly LanguageInfo Italian = new()
    {
        Code = "it",
        Name = "Italian",
        WritingSystem = WritingSystem.Alphabetic,
        RequiresSpaceSeparation = true,
        HasParticles = false,
        IsRightToLeft = false
    };
    
    public static readonly LanguageInfo Portuguese = new()
    {
        Code = "pt",
        Name = "Portuguese",
        WritingSystem = WritingSystem.Alphabetic,
        RequiresSpaceSeparation = true,
        HasParticles = false,
        IsRightToLeft = false
    };
    
    public static readonly LanguageInfo Russian = new()
    {
        Code = "ru",
        Name = "Russian",
        WritingSystem = WritingSystem.Alphabetic,
        RequiresSpaceSeparation = true,
        HasParticles = false,
        IsRightToLeft = false
    };
    
    public static readonly LanguageInfo Arabic = new()
    {
        Code = "ar",
        Name = "Arabic",
        WritingSystem = WritingSystem.Abjad,
        RequiresSpaceSeparation = true,
        HasParticles = false,
        IsRightToLeft = true
    };
    
    public static readonly LanguageInfo Hindi = new()
    {
        Code = "hi",
        Name = "Hindi",
        WritingSystem = WritingSystem.Abugida,
        RequiresSpaceSeparation = true,
        HasParticles = false,
        IsRightToLeft = false
    };
    
    public static readonly LanguageInfo Unknown = new()
    {
        Code = "unknown",
        Name = "Unknown",
        WritingSystem = WritingSystem.Unknown,
        RequiresSpaceSeparation = true,
        HasParticles = false,
        IsRightToLeft = false
    };
}

/// <summary>
/// バッチOCR処理の実装クラス
/// Phase 2-B: OCRバッチ処理最適化とパフォーマンス向上
/// ⚡ 高性能非同期処理版 - パフォーマンス分析機能付き
/// </summary>
public sealed class BatchOcrProcessor(
    IOcrEngine ocrEngine, 
    IAsyncPerformanceAnalyzer? performanceAnalyzer = null,
    ILogger<BatchOcrProcessor>? logger = null) : IBatchOcrProcessor, IDisposable
{
    private readonly IOcrEngine _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
    private readonly IAsyncPerformanceAnalyzer? _performanceAnalyzer = performanceAnalyzer;
    private readonly ILogger<BatchOcrProcessor>? _logger = logger;
    private readonly CoordinateBasedLineBreakProcessor _lineBreakProcessor = new(
        logger as ILogger<CoordinateBasedLineBreakProcessor> ?? 
        Microsoft.Extensions.Logging.Abstractions.NullLogger<CoordinateBasedLineBreakProcessor>.Instance);
    private readonly ConfidenceBasedReprocessor _confidenceReprocessor = new(
        ocrEngine,
        logger as ILogger<ConfidenceBasedReprocessor> ?? 
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfidenceBasedReprocessor>.Instance);
    private readonly UniversalMisrecognitionCorrector _misrecognitionCorrector = new(
        logger as ILogger<UniversalMisrecognitionCorrector> ?? 
        Microsoft.Extensions.Logging.Abstractions.NullLogger<UniversalMisrecognitionCorrector>.Instance);
    
    private BatchOcrOptions _options = new();
    private readonly ConcurrentQueue<ProcessingMetric> _processingHistory = new();
    private bool _disposed;
    
    // パフォーマンス統計
    private long _totalProcessedCount;
    private double _totalProcessingTime;
    private readonly DateTime _startTime = DateTime.UtcNow;
    private int _errorCount;
    private readonly ConcurrentDictionary<int, TextChunk> _chunkCache = new();
    private readonly object _configLock = new();

    /// <summary>
    /// 画像をバッチ処理してテキストチャンクを取得（⚡ 高性能非同期版）
    /// </summary>
    public async Task<IReadOnlyList<TextChunk>> ProcessBatchAsync(
        IAdvancedImage image, 
        IntPtr windowHandle, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        // デバッグ: PerformanceAnalyzerの状態確認（Console + File）
        var debugMessage1 = $"🔍 [BATCH-DEBUG] _performanceAnalyzer != null: {_performanceAnalyzer != null}";
        var debugMessage2 = $"🔍 [BATCH-DEBUG] _performanceAnalyzer型: {_performanceAnalyzer?.GetType().Name ?? "null"}";
        var debugMessage3 = $"🔍 [BATCH-DEBUG] ProcessBatchAsync呼び出し開始 - {DateTime.Now:HH:mm:ss.fff}";
        
        System.Console.WriteLine(debugMessage1);
        System.Console.WriteLine(debugMessage2);
        System.Console.WriteLine(debugMessage3);
        
        // ファイル出力で確実にログを記録
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_batch_ocr.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {debugMessage1}{Environment.NewLine}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_batch_ocr.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {debugMessage2}{Environment.NewLine}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_batch_ocr.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {debugMessage3}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"🚨 [BATCH-DEBUG] ファイル出力エラー: {ex.Message}");
        }
        
        // パフォーマンス分析機能付きで実行
        if (_performanceAnalyzer != null)
        {
            IReadOnlyList<TextChunk> batchResult = [];
            var measurement = await _performanceAnalyzer.MeasureAsync(
                async ct => {
                    batchResult = await ProcessBatchInternalAsync(image, windowHandle, ct).ConfigureAwait(false);
                    return batchResult;
                },
                "BatchOcrProcessor.ProcessBatch",
                cancellationToken).ConfigureAwait(false);
            
            var perfMessage = $"📊 BatchOcr パフォーマンス測定完了 - 実行時間: {measurement.ExecutionTime.TotalMilliseconds}ms, 成功: {measurement.IsSuccessful}";
            _logger?.LogInformation(perfMessage);
            System.Console.WriteLine($"📊 [BATCH-PERF] {perfMessage}");
            
            // ファイル出力でパフォーマンス結果を確実に記録
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_batch_ocr.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {perfMessage}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"🚨 [BATCH-PERF] ファイル出力エラー: {ex.Message}");
            }
            
            // 🔍 [BATCH-DEBUG] 結果詳細ログ追加
            try
            {
                var debugResultMessage = $"🔍 [BATCH-DEBUG] measurement.IsSuccessful={measurement.IsSuccessful}, batchResult.Count={batchResult.Count}";
                System.Console.WriteLine(debugResultMessage);
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_batch_ocr.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {debugResultMessage}{Environment.NewLine}");
                    
                if (measurement.IsSuccessful && batchResult.Count == 0)
                {
                    var emptyResultMessage = "⚠️ [BATCH-DEBUG] ProcessBatchInternalAsyncは成功したが、結果が0個";
                    System.Console.WriteLine(emptyResultMessage);
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_batch_ocr.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {emptyResultMessage}{Environment.NewLine}");
                }
            }
            catch (Exception debugEx)
            {
                System.Console.WriteLine($"🚨 [BATCH-DEBUG] デバッグログエラー: {debugEx.Message}");
            }
            
            return measurement.IsSuccessful ? batchResult : [];
        }
        
        return await ProcessBatchInternalAsync(image, windowHandle, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// バッチ処理の内部実装（パフォーマンス測定対象）
    /// ⚡ Phase 0: OCR真の並列化実装
    /// </summary>
    private async Task<IReadOnlyList<TextChunk>> ProcessBatchInternalAsync(
        IAdvancedImage image, 
        IntPtr windowHandle, 
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var processingStartTime = DateTime.UtcNow;
        
        try
        {
            var overallTimer = Stopwatch.StartNew();
            var stageTimer = Stopwatch.StartNew();
            
            // パフォーマンス測定用の辞書を初期化
            var phaseTimers = new Dictionary<string, Stopwatch>();
            
            Console.WriteLine($"🔥 [STAGE-0] ProcessBatchInternalAsync開始 - 画像: {image.Width}x{image.Height}");
            
            // 🔍 [BATCH-DEBUG] ProcessBatchInternalAsync開始ログをファイルに出力
            try
            {
                var stageStartMessage = $"🔥 [STAGE-0] ProcessBatchInternalAsync開始 - 画像: {image.Width}x{image.Height}";
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_batch_ocr.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {stageStartMessage}{Environment.NewLine}");
            }
            catch (Exception debugEx)
            {
                System.Console.WriteLine($"🚨 [STAGE-0-DEBUG] ファイル出力エラー: {debugEx.Message}");
            }
            _logger?.LogInformation("⚡ 高性能バッチOCR処理開始 - 画像: {Width}x{Height}, ウィンドウ: {Handle}", 
                image.Width, image.Height, windowHandle.ToString("X", CultureInfo.InvariantCulture));

            // ⚡ Phase 0: 新しい並列化アプローチ
            stageTimer.Restart();
            Console.WriteLine($"🔥 [STAGE-1] 並列OCR開始 - 画像サイズ: {image.Width}x{image.Height}");
            
            // 画像を最適サイズのタイルに分割
            var optimalTileSize = _options.TileSize; // 設定可能なタイルサイズ
            stageTimer.Restart();
            Console.WriteLine($"🔥 [STAGE-2] タイル分割開始 - 目標サイズ: {optimalTileSize}x{optimalTileSize}");
            
            using var tileGenerationMeasurement = new Core.Performance.PerformanceMeasurement(
                Core.Performance.MeasurementType.ImageTileGeneration, 
                $"タイル分割処理 - 画像:{image.Width}x{image.Height}, 目標サイズ:{optimalTileSize}");
                
            var tiles = await SplitImageIntoOptimalTilesAsync(image, optimalTileSize).ConfigureAwait(false);
            
            var tileResult = tileGenerationMeasurement.Complete();
            Console.WriteLine($"🔥 [STAGE-2] タイル分割完了 - {tileResult.Duration.TotalMilliseconds:F1}ms, {tiles.Count}個のタイル");
            
            // タイル分割時間を記録
            var tileStopwatch = new Stopwatch();
            tileStopwatch.Start();
            tileStopwatch.Stop();
            // Elapsedプロパティは読み取り専用なので、ダミータイマーを作成して経過時間を設定
            var tileElapsedMs = (long)tileResult.Duration.TotalMilliseconds;
            phaseTimers["タイル分割"] = stageTimer;
            
            // 並列度制御付きOCR実行
            stageTimer.Restart();
            Console.WriteLine($"🔥 [STAGE-3] 並列OCR実行開始 - タイル数: {tiles.Count}, 並列度: {Environment.ProcessorCount}");
            using var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
            var parallelOcrTimer = Stopwatch.StartNew();
            
            var ocrTasks = tiles.Select(async (tile, index) =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var tileTimer = Stopwatch.StartNew();
                    Console.WriteLine($"🔥 [TILE-{index}] OCR開始 - 実際のタイルサイズ: {tile.Image.Width}x{tile.Image.Height}");
                    
                    // 各タイルでOCR実行（詳細時間測定）
                    using var ocrEngineExecution = new Core.Performance.PerformanceMeasurement(
                        Core.Performance.MeasurementType.OcrEngineExecution, 
                        $"PaddleOCR実行 - Tile{index}, サイズ:{tile.Image.Width}x{tile.Image.Height}");
                        
                    var result = await _ocrEngine.RecognizeAsync(tile.Image, null, cancellationToken).ConfigureAwait(false);
                    
                    var ocrEngineResult = ocrEngineExecution.Complete();
                    tileTimer.Stop();
                    Console.WriteLine($"🔥 [TILE-{index}] OCR完了 - {tileTimer.ElapsedMilliseconds}ms (エンジン:{ocrEngineResult.Duration.TotalMilliseconds:F1}ms), 検出領域数: {result.TextRegions?.Count ?? 0}");
                    
                    return new TileOcrResult
                    {
                        TileIndex = index,
                        TileOffset = tile.Offset,
                        Result = result,
                        ProcessingTime = tileTimer.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "🚨 タイルOCR処理に失敗 - Tile Index: {TileIndex}, Offset: ({X},{Y})", index, tile.Offset.X, tile.Offset.Y);
                    Console.WriteLine($"🚨 [TILE-{index}] OCR失敗 - エラー: {ex.Message}");
                    
                    // エラー時は空の結果を返すことで処理を継続
                    var dummyImage = new SimpleImageWrapper(tile.Width, tile.Height);
                    return new TileOcrResult
                    {
                        TileIndex = index,
                        TileOffset = tile.Offset,
                        Result = new OcrResults([], dummyImage, TimeSpan.Zero, "jpn"),
                        ProcessingTime = TimeSpan.Zero
                    };
                }
                finally
                {
                    semaphore.Release();
                    // タイル画像のリソース解放
                    tile.Image?.Dispose();
                }
            }).ToArray();
            
            // 全タイルのOCR完了を待機
            Console.WriteLine($"🔥 [STAGE-3] 並列OCRタスク待機開始");
            var tileResults = await Task.WhenAll(ocrTasks).ConfigureAwait(false);
            parallelOcrTimer.Stop();
            
            Console.WriteLine($"🔥 [STAGE-3] 並列OCR完了 - {stageTimer.ElapsedMilliseconds}ms全体時間, タイル数: {tileResults.Length}");
            
            // タイル結果をマージ
            stageTimer.Restart();
            Console.WriteLine($"🔥 [STAGE-4] タイル結果マージ開始");
            
            using var mergeResultsMeasurement = new Core.Performance.PerformanceMeasurement(
                Core.Performance.MeasurementType.OcrPostProcessing, 
                $"タイル結果マージ - タイル数:{tileResults.Length}, 画像:{image.Width}x{image.Height}");
                
            var mergeTimer = Stopwatch.StartNew();
            var mergedOcrResults = MergeTileResults(tileResults, image.Width, image.Height);
            mergeTimer.Stop();
            
            var mergeResult = mergeResultsMeasurement.Complete();
            Console.WriteLine($"🔥 [STAGE-4] マージ完了 - {stageTimer.ElapsedMilliseconds}ms (詳細:{mergeResult.Duration.TotalMilliseconds:F1}ms), 結果領域数: {mergedOcrResults.TextRegions.Count}");
            
            // パフォーマンス測定結果をphaseTimersに追加
            phaseTimers["ParallelOCR"] = parallelOcrTimer;
            phaseTimers["ResultMerge"] = mergeTimer;

            // ⚡ 旧い逐次処理を並列OCRに置き換え
            // var ocrResults = await ExecuteOcrWithOptimizationsAsync(image, qualityMetrics, cancellationToken).ConfigureAwait(false);
            var ocrResults = mergedOcrResults; // 並列OCRの結果を使用
            
            // メモリ解放を促進（連続OCR実行対策）
            if (_totalProcessedCount % 10 == 0) // 10回ごとにGC実行
            {
                _logger?.LogDebug("🧹 メモリ解放実行中...");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            
            // 3. テキストチャンクのグルーピング
            stageTimer.Restart();
            Console.WriteLine($"🔥 [STAGE-5] テキストチャンクのグルーピング開始");
            var groupingTimer = Stopwatch.StartNew();
            var initialTextChunks = await GroupTextIntoChunksAsync(ocrResults, windowHandle, cancellationToken).ConfigureAwait(false);
            groupingTimer.Stop();
            phaseTimers["TextGrouping"] = groupingTimer;
            Console.WriteLine($"🔥 [STAGE-5] チャンクグルーピング完了 - {stageTimer.ElapsedMilliseconds}ms, チャンク数: {initialTextChunks.Count}");
            
            // 4. 信頼度ベース再処理
            stageTimer.Restart();
            Console.WriteLine($"🔥 [STAGE-6] 信頼度ベース再処理開始");
            var reprocessTimer = Stopwatch.StartNew();
            var reprocessedChunks = await _confidenceReprocessor.ReprocessLowConfidenceChunksAsync(
                initialTextChunks, image, cancellationToken).ConfigureAwait(false);
            reprocessTimer.Stop();
            phaseTimers["ConfidenceReprocessing"] = reprocessTimer;
            Console.WriteLine($"🔥 [STAGE-6] 信頼度ベース再処理完了 - {stageTimer.ElapsedMilliseconds}ms, チャンク数: {reprocessedChunks.Count}");
            
            // 5. 普遍的誤認識修正 - 一時的に無効化してテスト
            stageTimer.Restart();
            Console.WriteLine($"🔥 [STAGE-7] 誤認識修正処理開始（スキップモード）");
            var correctionTimer = Stopwatch.StartNew();
            var textChunks = reprocessedChunks; // 誤認識修正をスキップ
            correctionTimer.Stop();
            phaseTimers["MisrecognitionCorrection"] = correctionTimer;
            Console.WriteLine($"🔥 [STAGE-7] 誤認識修正処理完了 - {stageTimer.ElapsedMilliseconds}ms (スキップ), 最終チャンク数: {textChunks.Count}");
            
            overallTimer.Stop();
            stopwatch.Stop();
            
            Console.WriteLine($"🔥 [STAGE-FINAL] ProcessBatchInternalAsync完了 - 総実行時間: {overallTimer.ElapsedMilliseconds}ms");
            
            // パフォーマンスサマリー出力
            System.Console.WriteLine($"\n📊 [PERF-SUMMARY] OCR処理完了 - 全体時間: {overallTimer.ElapsedMilliseconds}ms");
            System.Console.WriteLine("🔍 [PERF-BREAKDOWN] 段階別処理時間:");
            foreach (var phase in phaseTimers)
            {
                var percentage = phaseTimers.Values.Count > 0 ? (double)phase.Value.ElapsedMilliseconds / overallTimer.ElapsedMilliseconds * 100 : 0;
                System.Console.WriteLine($"  • {phase.Key}: {phase.Value.ElapsedMilliseconds}ms ({percentage:F1}%)");
            }
            System.Console.WriteLine($"📈 [PERF-SUMMARY] 最終結果: {textChunks.Count}個のテキストチャンク\n");
            
            // 6. パフォーマンス統計更新
            UpdatePerformanceMetrics(processingStartTime, stopwatch.Elapsed, textChunks.Count, true);
            
            // 7. BaketaLogManagerでOCR結果を構造化ログに記録
            try
            {
                var operationId = Guid.NewGuid().ToString("N")[..8];
                var averageConfidence = textChunks.Count > 0 
                    ? textChunks.Average(chunk => (double)chunk.AverageConfidence) 
                    : 0.0;
                var recognizedTexts = textChunks.Select(chunk => chunk.CombinedText).ToList();
                
                // パフォーマンス内訳を構築
                var performanceBreakdown = new Dictionary<string, double>();
                foreach (var phase in phaseTimers)
                {
                    performanceBreakdown[phase.Key] = phase.Value.ElapsedMilliseconds;
                }
                
                var ocrLogEntry = new OcrResultLogEntry
                {
                    OperationId = operationId,
                    Stage = "batch_processing_complete",
                    ImageSize = new Size(image.Width, image.Height),
                    TextRegionsFound = textChunks.Count,
                    AverageConfidence = averageConfidence,
                    ProcessingTimeMs = overallTimer.ElapsedMilliseconds,
                    PerformanceBreakdown = performanceBreakdown,
                    RecognizedTexts = recognizedTexts,
                    Engine = _ocrEngine.GetType().Name
                };
                
                BaketaLogManager.LogOcrResult(ocrLogEntry);
            }
            catch (Exception logEx)
            {
                _logger?.LogWarning(logEx, "OCR結果の構造化ログ記録に失敗");
            }
            
            _logger?.LogInformation("✅ バッチOCR処理完了 - 処理時間: {ElapsedMs}ms, チャンク数: {ChunkCount}", 
                stopwatch.ElapsedMilliseconds, textChunks.Count);

            return textChunks;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            UpdatePerformanceMetrics(processingStartTime, stopwatch.Elapsed, 0, false);
            
            _logger?.LogError(ex, "❌ バッチOCR処理エラー - 画像: {Width}x{Height}", image.Width, image.Height);
            throw;
        }
    }

    /// <summary>
    /// バッチ処理の設定を更新
    /// </summary>
    public async Task ConfigureBatchProcessingAsync(BatchOcrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        
        await Task.Run(() =>
        {
            lock (_configLock)
            {
                _options = options;
                _logger?.LogInformation("⚙️ バッチOCR設定更新 - 並列度: {Parallelism}, GPU: {GpuEnabled}", 
                    options.MaxParallelism, options.EnableGpuAcceleration);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// バッチ処理のパフォーマンスメトリクスを取得
    /// </summary>
    public BatchOcrMetrics GetPerformanceMetrics()
    {
        ThrowIfDisposed();
        
        lock (_configLock)
        {
            var totalProcessed = Interlocked.Read(ref _totalProcessedCount);
            var totalTime = _totalProcessingTime;
            var errorCount = _errorCount;
            var uptime = DateTime.UtcNow - _startTime;
            
            // 最近の処理履歴から統計計算
            var recentMetrics = _processingHistory.TakeLast(100).ToList();
            var successfulMetrics = recentMetrics.Where(m => m.Success).ToList();
            
            return new BatchOcrMetrics
            {
                TotalProcessedCount = totalProcessed,
                AverageProcessingTimeMs = totalProcessed > 0 ? totalTime / totalProcessed : 0,
                LastProcessingTimeMs = recentMetrics.LastOrDefault()?.ProcessingTimeMs ?? 0,
                AverageTextCount = successfulMetrics.Count > 0 ? successfulMetrics.Average(m => m.TextCount) : 0,
                AverageConfidence = successfulMetrics.Count > 0 ? successfulMetrics.Average(m => m.AverageConfidence) : 0,
                ParallelEfficiency = CalculateParallelEfficiency(),
                CacheHitRate = CalculateCacheHitRate(),
                MemoryUsageMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
                ErrorRate = totalProcessed > 0 ? (double)errorCount / totalProcessed : 0,
                GpuUtilization = _options.EnableGpuAcceleration ? 0.8 : null, // TODO: 実際のGPU使用率取得
                PreprocessingRatio = 0.15, // TODO: 実際の前処理時間比率
                OcrProcessingRatio = 0.70, // TODO: 実際のOCR処理時間比率
                PostprocessingRatio = 0.15  // TODO: 実際の後処理時間比率
            };
        }
    }

    /// <summary>
    /// バッチ処理キャッシュをクリア
    /// </summary>
    public async Task ClearCacheAsync()
    {
        ThrowIfDisposed();
        
        await Task.Run(() =>
        {
            _chunkCache.Clear();
            _logger?.LogInformation("🧹 バッチOCRキャッシュクリア完了");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 画像品質分析
    /// </summary>
    private async Task<ImageQualityMetrics> AnalyzeImageQualityAsync(IAdvancedImage image, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            // 簡易品質分析（実際の実装では詳細な画像分析を行う）
            var resolution = image.Width * image.Height;
            var aspectRatio = (double)image.Width / image.Height;
            
            var qualityScore = resolution switch
            {
                > 2000000 => 0.9, // 高解像度
                > 500000 => 0.7,  // 中解像度
                _ => 0.5           // 低解像度
            };

            // アスペクト比による調整
            if (aspectRatio is < 0.5 or > 3.0)
                qualityScore *= 0.8; // 極端なアスペクト比は品質を下げる

            var recommendedProcessing = qualityScore switch
            {
                >= 0.8 => ImageProcessingType.Standard,
                >= 0.6 => ImageProcessingType.Enhanced,
                _ => ImageProcessingType.Aggressive
            };

            return new ImageQualityMetrics(qualityScore, recommendedProcessing);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 最適化されたOCR実行
    /// </summary>
    private async Task<OcrResults> ExecuteOcrWithOptimizationsAsync(
        IAdvancedImage image, 
        ImageQualityMetrics qualityMetrics, 
        CancellationToken cancellationToken)
    {
        // 品質に応じた前処理パラメータ調整
        var processingOptions = qualityMetrics.RecommendedProcessing switch
        {
            ImageProcessingType.Aggressive => new { Threshold = 0.1, Enhancement = true },
            ImageProcessingType.Enhanced => new { Threshold = 0.15, Enhancement = true },
            _ => new { Threshold = 0.25, Enhancement = false }
        };

        _logger?.LogDebug("🔧 OCR前処理設定 - 閾値: {Threshold}, 強化: {Enhancement}", 
            processingOptions.Threshold, processingOptions.Enhancement);

        // OCR設定の動的調整
        System.Console.WriteLine("⚙️ Phase 6デバッグ: OCR設定取得開始");
        var currentSettings = _ocrEngine.GetSettings();
        System.Console.WriteLine("⚙️ Phase 6デバッグ: OCR設定取得完了");
        
        var optimizedSettings = currentSettings.Clone();
        optimizedSettings.DetectionThreshold = processingOptions.Threshold;

        System.Console.WriteLine("⚙️ Phase 6デバッグ: OCR設定適用開始");
        await _ocrEngine.ApplySettingsAsync(optimizedSettings, cancellationToken).ConfigureAwait(false);
        System.Console.WriteLine("⚙️ Phase 6デバッグ: OCR設定適用完了");

        try
        {
            System.Console.WriteLine("🎯 Phase 6デバッグ: OCRエンジンRecognizeAsync開始");
            
            // 🔍 画像サイズを詳細ログ
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} 🔍 OCRエンジンRecognizeAsync直前（直接書き込み）: 画像サイズ={image.Width}x{image.Height}, Format={image.Format}{Environment.NewLine}");
            }
            catch { }
            
            var result = await _ocrEngine.RecognizeAsync(image, cancellationToken: cancellationToken).ConfigureAwait(false);
            System.Console.WriteLine($"🎯 Phase 6デバッグ: OCRエンジンRecognizeAsync完了 - 検出領域数={result.TextRegions.Count}");
            return result;
        }
        finally
        {
            // 設定を元に戻す
            System.Console.WriteLine("🔄 Phase 6デバッグ: OCR設定復元開始");
            await _ocrEngine.ApplySettingsAsync(currentSettings, cancellationToken).ConfigureAwait(false);
            System.Console.WriteLine("🔄 Phase 6デバッグ: OCR設定復元完了");
        }
    }

    /// <summary>
    /// テキストをチャンクにグルーピング
    /// </summary>
    private async Task<IReadOnlyList<TextChunk>> GroupTextIntoChunksAsync(
        OcrResults ocrResults, 
        IntPtr windowHandle, 
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!ocrResults.HasText || ocrResults.TextRegions.Count == 0)
            {
                _logger?.LogDebug("📝 テキスト領域なし - 空のチャンクリストを返却");
                return (IReadOnlyList<TextChunk>)[];
            }

            var chunks = new List<TextChunk>();
            var processedRegions = new HashSet<OcrTextRegion>();
            var chunkId = 0;

            foreach (var region in ocrResults.TextRegions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (processedRegions.Contains(region))
                    continue;

                // 近接テキスト領域をグループ化
                var groupedRegions = FindNearbyRegions(region, ocrResults.TextRegions, processedRegions);
                processedRegions.UnionWith(groupedRegions);

                // PositionedTextResultに変換
                var positionedResults = groupedRegions.Select(r => new PositionedTextResult
                {
                    Text = r.Text,
                    BoundingBox = r.Bounds,
                    Confidence = (float)r.Confidence,
                    ChunkId = chunkId,
                    ProcessingTime = ocrResults.ProcessingTime,
                    DetectedLanguage = ocrResults.LanguageCode
                }).ToList();

                // チャンクのバウンディングボックス計算
                var combinedBounds = CalculateCombinedBounds(groupedRegions);
                
                // 座標情報ベースの改行処理を適用
                var rawCombinedText = CombineTextsIntelligently(groupedRegions, ocrResults.LanguageCode);
                var positionedTextChunks = positionedResults.Select(r => new TextChunk
                {
                    ChunkId = r.ChunkId,
                    TextResults = [r],
                    CombinedBounds = r.BoundingBox,
                    CombinedText = r.Text,
                    SourceWindowHandle = windowHandle,
                    DetectedLanguage = r.DetectedLanguage
                }).ToList();
                
                // 座標ベースの改行処理でテキストを最適化
                var combinedText = _lineBreakProcessor.ProcessLineBreaks(positionedTextChunks);

                var chunk = new TextChunk
                {
                    ChunkId = chunkId++,
                    TextResults = positionedResults,
                    CombinedBounds = combinedBounds,
                    CombinedText = combinedText,
                    SourceWindowHandle = windowHandle,
                    DetectedLanguage = ocrResults.LanguageCode
                };

                chunks.Add(chunk);

                _logger?.LogDebug("📦 チャンク作成 - ID: {ChunkId}, テキスト: '{Text}', 領域数: {RegionCount}", 
                    chunk.ChunkId, chunk.CombinedText, groupedRegions.Count);
                    
                // デバッグ用に詳細情報を出力
                System.Console.WriteLine($"🎯 チャンク#{chunk.ChunkId} - 位置: ({combinedBounds.X},{combinedBounds.Y}) サイズ: ({combinedBounds.Width}x{combinedBounds.Height}) テキスト: '{chunk.CombinedText}'");
            }

            // 空のテキストチャンクや無効なテキストをフィルタリング
            var validChunks = FilterValidTextChunks(chunks);
            
            _logger?.LogInformation("📊 チャンクグルーピング完了 - 総チャンク数: {ChunkCount}, 有効チャンク数: {ValidCount}, 総テキスト領域数: {RegionCount}", 
                chunks.Count, validChunks.Count, ocrResults.TextRegions.Count);

            return (IReadOnlyList<TextChunk>)validChunks.AsReadOnly();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 有効なテキストチャンクをフィルタリング
    /// </summary>
    /// <param name="chunks">元のチャンクリスト</param>
    /// <returns>有効なチャンクのみのリスト</returns>
    private List<TextChunk> FilterValidTextChunks(List<TextChunk> chunks)
    {
        var validChunks = new List<TextChunk>();
        
        foreach (var chunk in chunks)
        {
            // 空のテキストや無効なテキストをスキップ
            if (string.IsNullOrWhiteSpace(chunk.CombinedText))
            {
                _logger?.LogDebug("📝 空のテキストチャンクをスキップ: ChunkId={ChunkId}", chunk.ChunkId);
                continue;
            }
            
            // 単一文字で意味のないテキストをスキップ（設定可能）
            if (chunk.CombinedText.Trim().Length == 1 && IsNoiseSingleCharacter(chunk.CombinedText.Trim()))
            {
                _logger?.LogDebug("📝 ノイズ単一文字をスキップ: ChunkId={ChunkId}, Text='{Text}'", chunk.ChunkId, chunk.CombinedText);
                continue;
            }
            
            // 非常に小さな領域（ノイズの可能性）をスキップ
            if (chunk.CombinedBounds.Width < 5 || chunk.CombinedBounds.Height < 5)
            {
                _logger?.LogDebug("📝 極小領域をスキップ: ChunkId={ChunkId}, Size=({Width}x{Height})", 
                    chunk.ChunkId, chunk.CombinedBounds.Width, chunk.CombinedBounds.Height);
                continue;
            }
            
            // 信頼度が極端に低いテキストをスキップ
            var averageConfidence = chunk.TextResults.Count > 0 ? 
                chunk.TextResults.Average(r => r.Confidence) : 1.0f;
            
            if (averageConfidence < 0.1f) // 10%未満の信頼度
            {
                _logger?.LogDebug("📝 低信頼度テキストをスキップ: ChunkId={ChunkId}, Confidence={Confidence:F3}", 
                    chunk.ChunkId, averageConfidence);
                continue;
            }
            
            validChunks.Add(chunk);
            _logger?.LogDebug("✅ 有効テキストチャンク: ChunkId={ChunkId}, Text='{Text}', Confidence={Confidence:F3}", 
                chunk.ChunkId, chunk.CombinedText, averageConfidence);
        }
        
        return validChunks;
    }
    
    /// <summary>
    /// 単一文字がノイズかどうかを判定
    /// </summary>
    /// <param name="character">チェックする文字</param>
    /// <returns>ノイズと判定される場合true</returns>
    private static bool IsNoiseSingleCharacter(string character)
    {
        if (character.Length != 1)
            return false;
            
        var c = character[0];
        
        // 一般的なノイズ文字（記号、特殊文字）
        var noiseCharacters = new HashSet<char>
        {
            '.', ',', ':', ';', '!', '?', '-', '_', '=', '+', '*', '#', '@', 
            '(', ')', '[', ']', '{', '}', '<', '>', '/', '\\', '|', '~', '`',
            '１', '２', '３', '４', '５', '６', '７', '８', '９', '０', // 全角数字（単体ではノイズの可能性）
            '－', '＝', '＋', '＊', '＃', '＠', // 全角記号
            '　' // 全角スペース
        };
        
        // 制御文字や非印字文字
        if (char.IsControl(c) || char.IsWhiteSpace(c))
            return true;
            
        // ノイズ文字リストに含まれる
        if (noiseCharacters.Contains(c))
            return true;
            
        // ASCII範囲外の単一文字は有効とみなす（日本語、中国語等）
        return false;
    }

    /// <summary>
    /// 近接テキスト領域を検索（改良版：垂直方向と水平方向で異なる閾値を使用）
    /// </summary>
    private List<OcrTextRegion> FindNearbyRegions(
        OcrTextRegion baseRegion, 
        IReadOnlyList<OcrTextRegion> allRegions, 
        HashSet<OcrTextRegion> processedRegions)
    {
        var nearbyRegions = new List<OcrTextRegion> { baseRegion };
        
        // 大幅に拡張されたテキストグループ化: 折り返しテキストをより広範囲で認識
        var verticalThreshold = _options.ChunkGroupingDistance * 3.0; // 垂直方向を大幅拡張（複数行の段落対応）
        var horizontalThreshold = _options.ChunkGroupingDistance * 2.0; // 水平方向も拡張（長い文章対応）
        
        foreach (var region in allRegions)
        {
            if (processedRegions.Contains(region) || nearbyRegions.Contains(region))
                continue;

            // baseRegionとの距離と位置関係を計算
            var deltaX = Math.Abs(region.Bounds.X + region.Bounds.Width / 2 - (baseRegion.Bounds.X + baseRegion.Bounds.Width / 2));
            var deltaY = Math.Abs(region.Bounds.Y + region.Bounds.Height / 2 - (baseRegion.Bounds.Y + baseRegion.Bounds.Height / 2));
            
            // 水平方向に近い（同じ行）の場合 - より寛容な判定
            if (deltaY <= region.Bounds.Height * 1.0 && deltaX <= horizontalThreshold)
            {
                nearbyRegions.Add(region);
            }
            // 垂直方向に近い（次の行/折り返し）の場合 - 大幅に拡張された条件
            else if (IsTextWrappedOrNextLine(baseRegion, region, deltaY, verticalThreshold))
            {
                nearbyRegions.Add(region);
            }
            // 段落内の遠い行も検出（より広範囲のテキストブロック認識）
            else if (IsParagraphText(baseRegion, region, deltaY, verticalThreshold * 1.5))
            {
                nearbyRegions.Add(region);
            }
        }

        return nearbyRegions;
    }

    /// <summary>
    /// テキストが折り返しまたは次の行かどうかを判定（拡張版）
    /// </summary>
    /// <param name="baseRegion">基準テキスト領域</param>
    /// <param name="targetRegion">対象テキスト領域</param>
    /// <param name="deltaX">水平距離</param>
    /// <param name="deltaY">垂直距離</param>
    /// <param name="verticalThreshold">垂直閾値</param>
    /// <returns>折り返し/次行と判定される場合true</returns>
    private static bool IsTextWrappedOrNextLine(OcrTextRegion baseRegion, OcrTextRegion targetRegion, 
        double deltaY, double verticalThreshold)
    {
        // 基本的な垂直距離チェック（拡張）
        if (deltaY > verticalThreshold)
            return false;

        // 水平位置の重複または近接をチェック（折り返しテキストの特徴）
        var baseLeft = baseRegion.Bounds.Left;
        var baseRight = baseRegion.Bounds.Right;
        var targetLeft = targetRegion.Bounds.Left;
        var targetRight = targetRegion.Bounds.Right;

        // 水平方向のオーバーラップまたは近接判定（より寛容に）
        var horizontalOverlap = Math.Max(0, Math.Min(baseRight, targetRight) - Math.Max(baseLeft, targetLeft));
        var horizontalDistance = Math.Max(0, Math.Max(targetLeft - baseRight, baseLeft - targetRight));

        // 条件1: 垂直方向に近い（次の行）- より寛容な判定
        var isVerticallyClose = deltaY <= Math.Max(baseRegion.Bounds.Height, targetRegion.Bounds.Height) * 2.5;

        // 条件2: 水平方向で重複または適度に近い（同じテキストブロック内）- より寛容に
        var maxWidth = Math.Max(baseRegion.Bounds.Width, targetRegion.Bounds.Width);
        var isHorizontallyRelated = horizontalOverlap > 0 || horizontalDistance <= maxWidth * 0.8;

        // 条件3: 左端が揃っている（段落の開始位置が同じ）- より寛容に
        var isLeftAligned = Math.Abs(baseLeft - targetLeft) <= Math.Min(baseRegion.Bounds.Width, targetRegion.Bounds.Width) * 0.5;

        // 条件4: 右端が揃っている（右揃えテキスト対応）
        var isRightAligned = Math.Abs(baseRight - targetRight) <= Math.Min(baseRegion.Bounds.Width, targetRegion.Bounds.Width) * 0.5;

        // 条件5: センター揃い（中央揃えテキスト対応）
        var baseCenterX = baseLeft + baseRegion.Bounds.Width / 2;
        var targetCenterX = targetLeft + targetRegion.Bounds.Width / 2;
        var isCenterAligned = Math.Abs(baseCenterX - targetCenterX) <= Math.Min(baseRegion.Bounds.Width, targetRegion.Bounds.Width) * 0.3;

        // 折り返しまたは次の行と判定（より多様な条件で）
        return isVerticallyClose && (isHorizontallyRelated || isLeftAligned || isRightAligned || isCenterAligned);
    }

    /// <summary>
    /// 同一段落内のテキストかどうかを判定（より広範囲）
    /// </summary>
    /// <param name="baseRegion">基準テキスト領域</param>
    /// <param name="targetRegion">対象テキスト領域</param>
    /// <param name="deltaX">水平距離</param>
    /// <param name="deltaY">垂直距離</param>
    /// <param name="extendedVerticalThreshold">拡張垂直閾値</param>
    /// <returns>同一段落と判定される場合true</returns>
    private static bool IsParagraphText(OcrTextRegion baseRegion, OcrTextRegion targetRegion, 
        double deltaY, double extendedVerticalThreshold)
    {
        // 非常に遠い場合は段落が異なる
        if (deltaY > extendedVerticalThreshold)
            return false;

        var baseLeft = baseRegion.Bounds.Left;
        var baseRight = baseRegion.Bounds.Right;
        var targetLeft = targetRegion.Bounds.Left;
        var targetRight = targetRegion.Bounds.Right;

        // 段落レベルでの位置関係判定
        var paragraphWidth = Math.Max(baseRegion.Bounds.Width, targetRegion.Bounds.Width) * 2;
        
        // 条件1: 水平方向で大きく重複または近接している
        var horizontalOverlap = Math.Max(0, Math.Min(baseRight, targetRight) - Math.Max(baseLeft, targetLeft));
        var isInSameParagraphHorizontally = horizontalOverlap > 0 || 
                                          Math.Abs(baseLeft - targetLeft) <= paragraphWidth * 0.5;

        // 条件2: 垂直方向で段落内の距離範囲内
        var maxHeight = Math.Max(baseRegion.Bounds.Height, targetRegion.Bounds.Height);
        var isInSameParagraphVertically = deltaY <= maxHeight * 4.0; // 4行分程度まで許容

        // 条件3: テキストサイズが類似している（同じフォント・同じ文書の可能性）
        var heightRatio = Math.Min(baseRegion.Bounds.Height, targetRegion.Bounds.Height) / 
                         Math.Max(baseRegion.Bounds.Height, targetRegion.Bounds.Height);
        var isSimilarSize = heightRatio >= 0.5; // 高さが50%以上類似

        return isInSameParagraphHorizontally && isInSameParagraphVertically && isSimilarSize;
    }

    /// <summary>
    /// 2つのテキスト領域間の距離を計算
    /// </summary>
    private static double CalculateDistance(Rectangle rect1, Rectangle rect2)
    {
        var center1 = new Point(rect1.X + rect1.Width / 2, rect1.Y + rect1.Height / 2);
        var center2 = new Point(rect2.X + rect2.Width / 2, rect2.Y + rect2.Height / 2);
        
        var dx = center1.X - center2.X;
        var dy = center1.Y - center2.Y;
        
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// インテリジェントなテキスト結合（言語と位置を考慮した多言語対応）
    /// </summary>
    /// <param name="regions">テキスト領域のリスト</param>
    /// <param name="languageCode">検出された言語コード</param>
    /// <returns>結合されたテキスト</returns>
    private static string CombineTextsIntelligently(List<OcrTextRegion> regions, string? languageCode)
    {
        if (regions.Count == 0)
            return string.Empty;
            
        if (regions.Count == 1)
            return ApplyLanguageSpecificCorrections(regions[0].Text, languageCode);
            
        // 位置でソート（左上から右下へ）
        var sortedRegions = regions
            .OrderBy(r => r.Bounds.Y)  // まず縦方向でソート
            .ThenBy(r => r.Bounds.X)   // 次に横方向でソート
            .ToList();
            
        var languageInfo = GetLanguageInfo(languageCode);
        
        return CombineTextByLanguageRules(sortedRegions, languageInfo);
    }
    
    /// <summary>
    /// 言語情報を取得
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>言語情報</returns>
    private static LanguageInfo GetLanguageInfo(string? languageCode)
    {
        if (string.IsNullOrEmpty(languageCode))
            return LanguageInfo.Japanese; // デフォルト

        var normalizedCode = languageCode.ToLowerInvariant();
        
        return normalizedCode switch
        {
            var code when code.StartsWith("ja", StringComparison.Ordinal) || code.StartsWith("jp", StringComparison.Ordinal) => LanguageInfo.Japanese,
            var code when code.StartsWith("en", StringComparison.Ordinal) => LanguageInfo.English,
            var code when code.StartsWith("zh", StringComparison.Ordinal) || code.StartsWith("cn", StringComparison.Ordinal) => LanguageInfo.Chinese,
            var code when code.StartsWith("ko", StringComparison.Ordinal) || code.StartsWith("kr", StringComparison.Ordinal) => LanguageInfo.Korean,
            var code when code.StartsWith("de", StringComparison.Ordinal) => LanguageInfo.German,
            var code when code.StartsWith("fr", StringComparison.Ordinal) => LanguageInfo.French,
            var code when code.StartsWith("es", StringComparison.Ordinal) => LanguageInfo.Spanish,
            var code when code.StartsWith("it", StringComparison.Ordinal) => LanguageInfo.Italian,
            var code when code.StartsWith("pt", StringComparison.Ordinal) => LanguageInfo.Portuguese,
            var code when code.StartsWith("ru", StringComparison.Ordinal) => LanguageInfo.Russian,
            var code when code.StartsWith("ar", StringComparison.Ordinal) => LanguageInfo.Arabic,
            var code when code.StartsWith("hi", StringComparison.Ordinal) => LanguageInfo.Hindi,
            _ => LanguageInfo.Unknown
        };
    }
    
    /// <summary>
    /// 言語ルールに従ってテキストを結合
    /// </summary>
    /// <param name="regions">位置順にソートされたテキスト領域</param>
    /// <param name="languageInfo">言語情報</param>
    /// <returns>結合されたテキスト</returns>
    private static string CombineTextByLanguageRules(List<OcrTextRegion> regions, LanguageInfo languageInfo)
    {
        var textParts = regions.Select(r => r.Text.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
        
        if (textParts.Count == 0)
            return string.Empty;
            
        if (textParts.Count == 1)
            return ApplyLanguageSpecificCorrections(textParts[0], languageInfo.Code);
            
        var result = new System.Text.StringBuilder();
        
        for (int i = 0; i < textParts.Count; i++)
        {
            var currentText = ApplyLanguageSpecificCorrections(textParts[i], languageInfo.Code);
            result.Append(currentText);
            
            // 次のテキストとの結合条件をチェック
            if (i < textParts.Count - 1)
            {
                var nextText = textParts[i + 1];
                var separator = GetTextSeparator(currentText, nextText, languageInfo);
                result.Append(separator);
            }
        }
        
        return result.ToString();
    }
    
    /// <summary>
    /// 言語固有の修正を適用
    /// </summary>
    /// <param name="text">元のテキスト</param>
    /// <param name="languageCode">言語コード</param>
    /// <returns>修正されたテキスト</returns>
    private static string ApplyLanguageSpecificCorrections(string text, string? languageCode)
    {
        if (string.IsNullOrEmpty(text))
            return text;
            
        var languageInfo = GetLanguageInfo(languageCode);
        
        return languageInfo.WritingSystem switch
        {
            WritingSystem.Logographic => CorrectLogographicText(text), // 日本語、中国語
            WritingSystem.Alphabetic => CorrectAlphabeticText(text),   // 英語、ドイツ語等
            WritingSystem.Syllabic => CorrectSyllabicText(text),       // 韓国語
            WritingSystem.Abjad => CorrectAbjadText(text),             // アラビア語
            WritingSystem.Abugida => CorrectAbugidaText(text),         // ヒンディー語
            _ => text
        };
    }
    
    /// <summary>
    /// テキスト間の区切り文字を取得
    /// </summary>
    /// <param name="currentText">現在のテキスト</param>
    /// <param name="nextText">次のテキスト</param>
    /// <param name="languageInfo">言語情報</param>
    /// <returns>適切な区切り文字</returns>
    private static string GetTextSeparator(string currentText, string nextText, LanguageInfo languageInfo)
    {
        // 文の終わりの場合
        if (IsEndOfSentence(currentText, languageInfo))
            return string.Empty;
            
        // 言語固有の結合ルール
        return languageInfo.WritingSystem switch
        {
            WritingSystem.Logographic => ShouldCombineDirectlyLogographic(currentText, nextText, languageInfo) ? "" : "",
            WritingSystem.Alphabetic => ShouldCombineDirectlyAlphabetic(currentText, nextText) ? "" : " ",
            WritingSystem.Syllabic => ShouldCombineDirectlySyllabic(currentText, nextText, languageInfo) ? "" : " ",
            WritingSystem.Abjad => " ", // アラビア語等は通常スペース区切り
            WritingSystem.Abugida => " ", // ヒンディー語等は通常スペース区切り
            _ => " "
        };
    }

    /// <summary>
    /// 日本語テキストの結合（適切な助詞・接続詞の復元を含む）
    /// </summary>
    /// <param name="regions">位置順にソートされたテキスト領域</param>
    /// <returns>結合されたテキスト</returns>
    [Obsolete("Use CombineTextByLanguageRules instead")]
    private static string CombineJapaneseText(List<OcrTextRegion> regions)
    {
        var textParts = regions.Select(r => r.Text.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
        
        if (textParts.Count == 0)
            return string.Empty;
            
        if (textParts.Count == 1)
            return textParts[0];
            
        var result = new System.Text.StringBuilder();
        
        for (int i = 0; i < textParts.Count; i++)
        {
            var currentText = textParts[i];
            
            // 既知の文字誤認識パターンを修正
            currentText = CorrectCommonMisrecognitions(currentText);
            
            result.Append(currentText);
            
            // 次のテキストとの結合条件をチェック
            if (i < textParts.Count - 1)
            {
                var nextText = textParts[i + 1];
                
                // 助詞・疑問詞の処理（「か」「が」「は」「を」等）
                if (ShouldCombineDirectly(currentText, nextText))
                {
                    // スペースなしで直接結合
                    continue;
                }
                
                // 文の境界でない場合は結合
                if (!IsEndOfSentence(currentText))
                {
                    // 改行が必要な場合を除いてスペースなしで結合
                    continue;
                }
            }
        }
        
        return result.ToString();
    }
    
    /// <summary>
    /// よくある文字誤認識パターンを修正
    /// </summary>
    /// <param name="text">元のテキスト</param>
    /// <returns>修正されたテキスト</returns>
    private static string CorrectCommonMisrecognitions(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
            
        // よくある誤認識パターンの辞書
        var corrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "加", "か" },  // ユーザー報告の問題
            { "力", "カ" },
            { "夕", "タ" },
            { "卜", "ト" },
            { "ロ", "口" },
            { "工", "エ" },
            { "人", "入" },
            { "二", "ニ" },
            { "八", "ハ" },
            { "入", "人" },
            { "木", "本" },
            { "日", "目" },
            { "月", "用" },
        };
        
        var correctedText = text;
        
        // 完全一致の修正
        if (corrections.TryGetValue(text, out var directCorrection))
        {
            return directCorrection;
        }
        
        // 部分的な修正（文末の助詞等）
        foreach (var (wrong, correct) in corrections)
        {
            if (text.EndsWith(wrong, StringComparison.OrdinalIgnoreCase))
            {
                correctedText = text[..^wrong.Length] + correct;
                break;
            }
        }
        
        return correctedText;
    }
    
    /// <summary>
    /// 2つのテキストを直接結合すべきかどうかを判定
    /// </summary>
    /// <param name="currentText">現在のテキスト</param>
    /// <param name="nextText">次のテキスト</param>
    /// <returns>直接結合すべき場合はtrue</returns>
    private static bool ShouldCombineDirectly(string currentText, string nextText)
    {
        if (string.IsNullOrEmpty(currentText) || string.IsNullOrEmpty(nextText))
            return false;
            
        // 助詞・疑問詞・語尾が分離されている場合
        var particlesAndEndings = new HashSet<string> 
        { 
            "か", "が", "は", "を", "に", "へ", "と", "で", "から", "まで", "より", "だ", "である", "です", "ます",
            "た", "て", "な", "ね", "よ", "ら", "り", "る", "ど", "ば", "ん", "う", "い", "え", "お"
        };
        
        // 次のテキストが助詞・語尾の場合
        if (particlesAndEndings.Contains(nextText))
            return true;
            
        // 現在のテキストが未完了の動詞・形容詞の場合
        var incompleteEndings = new HashSet<string> 
        { 
            "だっ", "であ", "でし", "まし", "いっ", "やっ", "きっ", "つっ", "とっ" 
        };
        
        if (incompleteEndings.Any(ending => currentText.EndsWith(ending, StringComparison.Ordinal)))
            return true;
            
        return false;
    }
    
    /// <summary>
    /// 文の終わりかどうかを判定
    /// </summary>
    /// <param name="text">テキスト</param>
    /// <returns>文の終わりの場合はtrue</returns>
    private static bool IsEndOfSentence(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
            
        var sentenceEnders = new HashSet<char> { '。', '！', '？', '!', '?' };
        return sentenceEnders.Contains(text[^1]);
    }

    /// <summary>
    /// 複数の領域の結合バウンディングボックスを計算
    /// </summary>
    private static Rectangle CalculateCombinedBounds(List<OcrTextRegion> regions)
    {
        if (regions.Count == 0)
            return Rectangle.Empty;

        var minX = regions.Min(r => r.Bounds.X);
        var minY = regions.Min(r => r.Bounds.Y);
        var maxX = regions.Max(r => r.Bounds.Right);
        var maxY = regions.Max(r => r.Bounds.Bottom);

        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// パフォーマンス統計を更新
    /// </summary>
    private void UpdatePerformanceMetrics(DateTime startTime, TimeSpan processingTime, int textCount, bool success)
    {
        lock (_configLock)
        {
            Interlocked.Increment(ref _totalProcessedCount);
            _totalProcessingTime += processingTime.TotalMilliseconds;
            
            if (!success)
                Interlocked.Increment(ref _errorCount);

            var metric = new ProcessingMetric
            {
                Timestamp = startTime,
                ProcessingTimeMs = processingTime.TotalMilliseconds,
                TextCount = textCount,
                Success = success,
                AverageConfidence = success ? 0.85 : 0 // TODO: 実際の信頼度
            };

            _processingHistory.Enqueue(metric);

            // 履歴のサイズ制限
            while (_processingHistory.Count > 1000)
                _processingHistory.TryDequeue(out _);
        }
    }

    /// <summary>
    /// 並列処理効率を計算
    /// </summary>
    private double CalculateParallelEfficiency()
    {
        // TODO: 実際の並列処理効率測定
        return Math.Min(1.0, _options.MaxParallelism / (double)Environment.ProcessorCount);
    }

    /// <summary>
    /// キャッシュヒット率を計算
    /// </summary>
    private double CalculateCacheHitRate()
    {
        // TODO: 実際のキャッシュ統計
        return 0.15; // 仮の値
    }

    #region 多言語対応の文字体系別修正メソッド
    
    /// <summary>
    /// 表意文字（日本語・中国語）の修正
    /// </summary>
    private static string CorrectLogographicText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
            
        // 日本語と中国語共通の漢字誤認識パターン
        var logographicCorrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ユーザー報告の問題
            { "加", "か" },
            
            // 一般的な漢字誤認識パターン
            { "力", "カ" }, { "夕", "タ" }, { "卜", "ト" },
            { "工", "エ" }, { "人", "入" }, { "二", "ニ" },
            { "八", "ハ" }, { "木", "本" }, { "日", "目" },
            { "月", "用" }, { "石", "右" }, { "白", "自" },
            { "立", "位" }, { "古", "吉" }, { "土", "士" },
            { "千", "干" }, { "万", "方" }, { "五", "王" }
        };
        
        return ApplyCorrections(text, logographicCorrections);
    }
    
    /// <summary>
    /// アルファベット文字の修正
    /// </summary>
    private static string CorrectAlphabeticText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
            
        // アルファベット誤認識パターン
        var alphabeticCorrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // よくある英語OCR誤認識
            { "rn", "m" }, { "cl", "d" }, { "vv", "w" },
            { "0", "O" }, { "1", "l" }, { "1", "I" },
            { "5", "S" }, { "6", "G" }, { "8", "B" },
            { "l", "1" }, { "I", "1" }, { "O", "0" },
            { "B", "8" }, { "G", "6" }, { "S", "5" }
        };
        
        return ApplyCorrections(text, alphabeticCorrections);
    }
    
    /// <summary>
    /// 音節文字（韓国語）の修正
    /// </summary>
    private static string CorrectSyllabicText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
            
        // ハングル誤認識パターン（基本的なもの）
        var syllabicCorrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 一般的なハングル誤認識
            { "ㅁ", "모" }, { "ㅇ", "오" }, { "ㅍ", "포" },
            { "ㄱ", "고" }, { "ㄴ", "노" }, { "ㄷ", "도" }
        };
        
        return ApplyCorrections(text, syllabicCorrections);
    }
    
    /// <summary>
    /// 子音文字（アラビア語）の修正
    /// </summary>
    private static string CorrectAbjadText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
            
        // アラビア語は複雑な文脈依存変形があるため、基本的な修正のみ
        return text.Trim();
    }
    
    /// <summary>
    /// アブギダ（ヒンディー語等）の修正
    /// </summary>
    private static string CorrectAbugidaText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
            
        // デーヴァナーガリー文字は複雑な合字があるため、基本的な修正のみ
        return text.Trim();
    }
    
    /// <summary>
    /// 表意文字の直接結合判定
    /// </summary>
    private static bool ShouldCombineDirectlyLogographic(string currentText, string nextText, LanguageInfo languageInfo)
    {
        if (string.IsNullOrEmpty(currentText) || string.IsNullOrEmpty(nextText))
            return false;
            
        if (languageInfo.Code == "ja")
        {
            // 日本語の助詞・語尾判定
            var japaneseParticles = new HashSet<string>
            {
                "か", "が", "は", "を", "に", "へ", "と", "で", "から", "まで", "より",
                "だ", "である", "です", "ます", "た", "て", "な", "ね", "よ"
            };
            
            return japaneseParticles.Contains(nextText);
        }
        
        // 中国語等は基本的に直接結合
        return true;
    }
    
    /// <summary>
    /// アルファベット文字の直接結合判定
    /// </summary>
    private static bool ShouldCombineDirectlyAlphabetic(string currentText, string nextText)
    {
        if (string.IsNullOrEmpty(currentText) || string.IsNullOrEmpty(nextText))
            return false;
            
        // アポストロフィや短縮形の場合
#pragma warning disable CA1865 // Unicode文字のため文字列が必要
        if (nextText.StartsWith("'", StringComparison.Ordinal) || nextText.StartsWith("'", StringComparison.Ordinal))
#pragma warning restore CA1865
            return true;
            
        // ハイフンで分割された単語
        if (currentText.EndsWith('-') || nextText.StartsWith('-'))
            return true;
            
        return false;
    }
    
    /// <summary>
    /// 音節文字の直接結合判定
    /// </summary>
    private static bool ShouldCombineDirectlySyllabic(string currentText, string nextText, LanguageInfo languageInfo)
    {
        if (string.IsNullOrEmpty(currentText) || string.IsNullOrEmpty(nextText))
            return false;
            
        if (languageInfo.Code == "ko")
        {
            // 韓国語の助詞判定（簡易版）
            var koreanParticles = new HashSet<string>
            {
                "은", "는", "이", "가", "을", "를", "에", "에서", "로", "과", "와"
            };
            
            return koreanParticles.Contains(nextText);
        }
        
        return false;
    }
    
    /// <summary>
    /// 文の終わり判定（多言語対応）
    /// </summary>
    private static bool IsEndOfSentence(string text, LanguageInfo languageInfo)
    {
        if (string.IsNullOrEmpty(text))
            return false;
            
        var lastChar = text[^1];
        
        // 共通の文末記号
        if (lastChar is '.' or '!' or '?')
            return true;
            
        // 言語固有の文末記号
        return languageInfo.Code switch
        {
            "ja" => lastChar is '。' or '！' or '？',
            "zh" => lastChar is '。' or '！' or '？',
            "ar" => lastChar is '.' or '؟' or '！',
            _ => false
        };
    }
    
    /// <summary>
    /// 修正辞書を適用
    /// </summary>
    private static string ApplyCorrections(string text, Dictionary<string, string> corrections)
    {
        if (string.IsNullOrEmpty(text) || corrections.Count == 0)
            return text;
            
        var correctedText = text;
        
        // 完全一致の修正
        if (corrections.TryGetValue(text, out var directCorrection))
        {
            return directCorrection;
        }
        
        // 部分的な修正（文末等）
        foreach (var (wrong, correct) in corrections)
        {
            if (text.EndsWith(wrong, StringComparison.OrdinalIgnoreCase))
            {
                correctedText = text[..^wrong.Length] + correct;
                break;
            }
        }
        
        return correctedText;
    }
    
    #endregion

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 画像を最適なタイルサイズに分割
    /// ⚡ Phase 0: OCR並列化のためのタイル分割ロジック
    /// </summary>
    private static async Task<List<ImageTile>> SplitImageIntoOptimalTilesAsync(IAdvancedImage image, int optimalTileSize)
    {
        var tiles = new List<ImageTile>();
        var tileIndex = 0;

        // 画像サイズがタイルサイズより小さい場合はそのまま使用
        if (image.Width <= optimalTileSize && image.Height <= optimalTileSize)
        {
            return [new ImageTile
            {
                Image = image,
                Offset = Point.Empty,
                Width = image.Width,
                Height = image.Height,
                TileIndex = 0
            }];
        }

        // X方向とY方向のタイル数を計算
        var tilesX = (int)Math.Ceiling((double)image.Width / optimalTileSize);
        var tilesY = (int)Math.Ceiling((double)image.Height / optimalTileSize);

        Console.WriteLine($"🔥 [TILE-SPLIT] 実際の画像分割開始 - 元画像: {image.Width}x{image.Height}, タイル: {tilesX}x{tilesY} = {tilesX * tilesY}個");

        for (var y = 0; y < tilesY; y++)
        {
            for (var x = 0; x < tilesX; x++)
            {
                var startX = x * optimalTileSize;
                var startY = y * optimalTileSize;
                var width = Math.Min(optimalTileSize, image.Width - startX);
                var height = Math.Min(optimalTileSize, image.Height - startY);

                // ⚡ 重要修正: タイムアウト監視付きExtractRegionAsync
                var tileRectangle = new Rectangle(startX, startY, width, height);
                var extractTimer = Stopwatch.StartNew();
                Console.WriteLine($"🔥 [TILE-{tileIndex}] 画像切り出し開始 - 位置: ({startX},{startY}), サイズ: {width}x{height}");

                var croppedImage = await image.ExtractRegionAsync(tileRectangle).ConfigureAwait(false);
                extractTimer.Stop();
                
                Console.WriteLine($"🔥 [TILE-{tileIndex}] 画像切り出し完了 - 実行時間: {extractTimer.ElapsedMilliseconds}ms");
                
                // ⚠️ 異常な遅延を検出してログに記録
                if (extractTimer.ElapsedMilliseconds > 1000) // 1秒を超える場合は異常
                {
                    Console.WriteLine($"🚨 [TILE-{tileIndex}] 異常な遅延検出！ ExtractRegionAsync実行時間: {extractTimer.ElapsedMilliseconds}ms");
                }

                tiles.Add(new ImageTile
                {
                    Image = croppedImage, // 実際に切り出された画像
                    Offset = new Point(startX, startY),
                    Width = width,
                    Height = height,
                    TileIndex = tileIndex++
                });
            }
        }

        Console.WriteLine($"🔥 [TILE-SPLIT] 画像分割完了 - {tiles.Count}個のタイルを作成");
        return tiles;
    }

    /// <summary>
    /// タイル結果をマージして単一のOCR結果に統合
    /// ⚡ Phase 0: 並列OCR結果の統合ロジック
    /// </summary>
    private static OcrResults MergeTileResults(TileOcrResult[] tileResults, int originalWidth, int originalHeight)
    {
        var allTextRegions = new List<OcrTextRegion>();
        var totalProcessingTime = TimeSpan.Zero;
        var allConfidences = new List<double>();

        foreach (var tileResult in tileResults.OrderBy(t => t.TileIndex))
        {
            totalProcessingTime += tileResult.ProcessingTime;

            // OcrResultsからOcrTextRegionを取得
            if (tileResult.Result?.TextRegions != null)
            {
                foreach (var region in tileResult.Result.TextRegions)
                {
                    // タイルオフセットを考慮してテキスト領域の座標を調整
                    var adjustedRegion = new OcrTextRegion(
                        region.Text,
                        new Rectangle(
                            region.Bounds.X + tileResult.TileOffset.X,
                            region.Bounds.Y + tileResult.TileOffset.Y,
                            region.Bounds.Width,
                            region.Bounds.Height
                        ),
                        region.Confidence,
                        region.Contour,
                        region.Direction
                    );
                    allTextRegions.Add(adjustedRegion);

                    // 信頼度情報を収集
                    allConfidences.Add(region.Confidence);
                }
            }
        }

        // 統合されたOCR結果を作成
        // 仮のIImageオブジェクト作成（実装では適切な画像オブジェクトを使用）
        var dummyImage = new SimpleImageWrapper(originalWidth, originalHeight);
        
        return new OcrResults(
            allTextRegions,
            dummyImage,
            totalProcessingTime,
            "jpn", // 日本語固定
            null, // regionOfInterest
            null  // mergedText
        );
    }


    public void Dispose()
    {
        if (_disposed) return;
        
        _chunkCache.Clear();
        _disposed = true;
        
        _logger?.LogInformation("🧹 BatchOcrProcessor リソース解放完了");
    }
}

/// <summary>
/// 画像品質メトリクス
/// </summary>
internal sealed record ImageQualityMetrics(double QualityScore, ImageProcessingType RecommendedProcessing);

/// <summary>
/// 画像処理タイプ
/// </summary>
internal enum ImageProcessingType
{
    Standard,   // 標準処理
    Enhanced,   // 強化処理
    Aggressive  // 積極的処理
}

/// <summary>
/// 処理メトリック
/// </summary>
internal sealed record ProcessingMetric
{
    public DateTime Timestamp { get; init; }
    public double ProcessingTimeMs { get; init; }
    public int TextCount { get; init; }
    public bool Success { get; init; }
    public double AverageConfidence { get; init; }
}

/// <summary>
/// 画像タイル情報
/// </summary>
internal sealed class ImageTile
{
    public required IAdvancedImage Image { get; init; }
    public required Point Offset { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int TileIndex { get; init; }
}

/// <summary>
/// タイルOCR結果
/// </summary>
internal sealed class TileOcrResult
{
    public required int TileIndex { get; init; }
    public required Point TileOffset { get; init; }
    public required OcrResults Result { get; init; }
    public required TimeSpan ProcessingTime { get; init; }
}

/// <summary>
/// 簡易画像ラッパー（OCR結果作成用）
/// </summary>
internal sealed class SimpleImageWrapper(int width, int height) : IImage
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public ImageFormat Format => ImageFormat.Rgba32;

    public IImage Clone()
    {
        return new SimpleImageWrapper(Width, Height);
    }

    public Task<IImage> ResizeAsync(int width, int height)
    {
        return Task.FromResult<IImage>(new SimpleImageWrapper(width, height));
    }

    public Task<byte[]> ToByteArrayAsync()
    {
        // 空のバイト配列を返す（実際のOCR処理では使用されない）
        return Task.FromResult(new byte[Width * Height * 4]); // BGRA32形式
    }

    public void Dispose()
    {
        // 何もしない
    }
}
