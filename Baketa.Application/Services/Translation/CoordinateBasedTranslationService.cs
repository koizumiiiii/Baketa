using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Translation.Models;
using Baketa.Core.Utilities;
using Baketa.Core.Performance;
using Baketa.Core.Logging;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.OCR;
using Baketa.Infrastructure.OCR.BatchProcessing;
using Baketa.Infrastructure.Translation.Local;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 座標ベース翻訳表示サービス
/// バッチOCR処理と複数ウィンドウオーバーレイ表示を統合した座標ベース翻訳システム
/// </summary>
public sealed class CoordinateBasedTranslationService : IDisposable
{
    private readonly IBatchOcrProcessor _batchOcrProcessor;
    private readonly IInPlaceTranslationOverlayManager _overlayManager;
    private readonly ITranslationService _translationService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<CoordinateBasedTranslationService>? _logger;
    private bool _disposed;

    public CoordinateBasedTranslationService(
        IBatchOcrProcessor batchOcrProcessor,
        IInPlaceTranslationOverlayManager overlayManager,
        ITranslationService translationService,
        IServiceProvider serviceProvider,
        IEventAggregator eventAggregator,
        ILogger<CoordinateBasedTranslationService>? logger = null)
    {
        _batchOcrProcessor = batchOcrProcessor ?? throw new ArgumentNullException(nameof(batchOcrProcessor));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger;
        
        // EventAggregator DI注入詳細デバッグ
        Console.WriteLine($"🔥 [DI_DEBUG] CoordinateBasedTranslationService初期化");
        Console.WriteLine($"🔥 [DI_DEBUG] EventAggregator型: {eventAggregator.GetType().FullName}");
        Console.WriteLine($"🔥 [DI_DEBUG] EventAggregatorハッシュ: {eventAggregator.GetHashCode()}");
        Console.WriteLine($"🔥 [DI_DEBUG] EventAggregator参照: {eventAggregator}");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DI_DEBUG] CoordinateBasedTranslationService初期化 - EventAggregator型: {eventAggregator.GetType().FullName}, ハッシュ: {eventAggregator.GetHashCode()}{Environment.NewLine}");
        
        _logger?.LogInformation("🚀 CoordinateBasedTranslationService initialized - Hash: {Hash}", this.GetHashCode());
    }

    /// <summary>
    /// 座標ベース翻訳処理を実行
    /// バッチOCR処理 → 複数ウィンドウオーバーレイ表示の統合フロー
    /// </summary>
    public async Task ProcessWithCoordinateBasedTranslationAsync(
        IAdvancedImage image, 
        IntPtr windowHandle,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            _logger?.LogInformation("🎯 座標ベース翻訳処理開始 - 画像: {Width}x{Height}, ウィンドウ: 0x{Handle:X}", 
                image.Width, image.Height, windowHandle.ToInt64());
            DebugLogUtility.WriteLog($"🎯 座標ベース翻訳処理開始 - 画像: {image.Width}x{image.Height}, ウィンドウ: 0x{windowHandle.ToInt64():X}");
            Console.WriteLine($"🎯 [DEBUG] ProcessWithCoordinateBasedTranslationAsync開始 - 画像: {image.Width}x{image.Height}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 [DEBUG] ProcessWithCoordinateBasedTranslationAsync開始 - 画像: {image.Width}x{image.Height}{Environment.NewLine}");

            // バッチOCR処理でテキストチャンクを取得（詳細時間測定）
            var ocrMeasurement = new PerformanceMeasurement(
                MeasurementType.BatchOcrProcessing, 
                $"バッチOCR処理 - 画像:{image.Width}x{image.Height}")
                .WithAdditionalInfo($"WindowHandle:0x{windowHandle.ToInt64():X}");
            
            var textChunks = await _batchOcrProcessor.ProcessBatchAsync(image, windowHandle, cancellationToken)
                .ConfigureAwait(false);
            
            var ocrResult = ocrMeasurement.Complete();
            var ocrProcessingTime = ocrResult.Duration;
            
            _logger?.LogInformation("✅ バッチOCR完了 - チャンク数: {ChunkCount}, 処理時間: {ProcessingTime}ms", 
                textChunks.Count, ocrProcessingTime.TotalMilliseconds);
            
            // OCR完了イベントを発行
            // 🚀 パフォーマンス最適化: EventAggregatorによる65秒の遅延を回避するため一時的に無効化
            // TODO: Phase 2でバッチ処理実装後に再検討
            // await PublishOcrCompletedEventAsync(image, textChunks, ocrProcessingTime).ConfigureAwait(false);
            
            // チャンクの詳細情報をデバッグ出力
            DebugLogUtility.WriteLog($"\n🔍 [CoordinateBasedTranslationService] バッチOCR結果詳細解析 (ウィンドウ: 0x{windowHandle.ToInt64():X}):");
            DebugLogUtility.WriteLog($"   入力画像サイズ: {image.Width}x{image.Height}");
            DebugLogUtility.WriteLog($"   検出されたテキストチャンク数: {textChunks.Count}");
            
            for (int i = 0; i < textChunks.Count; i++)
            {
                var chunk = textChunks[i];
                DebugLogUtility.WriteLog($"\n📍 チャンク[{i}] ID={chunk.ChunkId}");
                DebugLogUtility.WriteLog($"   OCR生座標: X={chunk.CombinedBounds.X}, Y={chunk.CombinedBounds.Y}");
                DebugLogUtility.WriteLog($"   OCR生サイズ: W={chunk.CombinedBounds.Width}, H={chunk.CombinedBounds.Height}");
                DebugLogUtility.WriteLog($"   元テキスト: '{chunk.CombinedText}'");
                DebugLogUtility.WriteLog($"   翻訳テキスト: '{chunk.TranslatedText}'");
                
                // 座標変換情報
                var overlayPos = chunk.GetOverlayPosition();
                var overlaySize = chunk.GetOverlaySize();
                DebugLogUtility.WriteLog($"   インプレース位置: ({overlayPos.X},{overlayPos.Y}) [元座標と同じ]");
                DebugLogUtility.WriteLog($"   インプレースサイズ: ({overlaySize.Width},{overlaySize.Height}) [元サイズと同じ]");
                DebugLogUtility.WriteLog($"   計算フォントサイズ: {chunk.CalculateOptimalFontSize()}px (Height {chunk.CombinedBounds.Height} * 0.45)");
                DebugLogUtility.WriteLog($"   インプレース表示可能: {chunk.CanShowInPlace()}");
                
                // TextResultsの詳細情報
                DebugLogUtility.WriteLog($"   構成TextResults数: {chunk.TextResults.Count}");
                for (int j = 0; j < Math.Min(chunk.TextResults.Count, 3); j++) // 最初の3個だけ表示
                {
                    var result = chunk.TextResults[j];
                    DebugLogUtility.WriteLog($"     [{j}] テキスト: '{result.Text}', 位置: ({result.BoundingBox.X},{result.BoundingBox.Y}), サイズ: ({result.BoundingBox.Width}x{result.BoundingBox.Height})");
                }
            }

            // 🚨 画面境界チェックと座標補正
            var screenBounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            var screenWidth = screenBounds.Width;
            var screenHeight = screenBounds.Height;
            
            for (int i = 0; i < textChunks.Count; i++)
            {
                var chunk = textChunks[i];
                var originalBounds = chunk.CombinedBounds;
                
                // 画面外座標をチェックし修正
                if (originalBounds.Y > screenHeight || originalBounds.X > screenWidth)
                {
                    var clampedX = Math.Max(0, Math.Min(originalBounds.X, screenWidth - originalBounds.Width));
                    var clampedY = Math.Max(0, Math.Min(originalBounds.Y, screenHeight - originalBounds.Height));
                    
                    DebugLogUtility.WriteLog($"🚨 画面外座標を修正: チャンク[{i}] 元座標({originalBounds.X},{originalBounds.Y}) → 補正後({clampedX},{clampedY}) [画面サイズ:{screenWidth}x{screenHeight}]");
                    
                    // チャンクの座標を修正（注：実際のチャンク座標修正は別途実装が必要）
                    // この段階ではログ出力のみで警告
                    DebugLogUtility.WriteLog($"⚠️ このテキストは画面外のため表示されません: '{chunk.CombinedText}'");
                }
            }

            if (textChunks.Count == 0)
            {
                _logger?.LogWarning("📝 テキストチャンクが0個のため、オーバーレイ表示をスキップ");
                DebugLogUtility.WriteLog("📝 テキストチャンクが0個のため、オーバーレイ表示をスキップ");
                return;
            }

            // OCR完了イベントは既に90行目で発行済み（二重発行バグ修正）
            
            // 実際の翻訳処理を実行（バッチ処理で高速化）
            _logger?.LogInformation("🌐 バッチ翻訳処理開始 - チャンク数: {Count}", textChunks.Count);
            DebugLogUtility.WriteLog($"🌐 バッチ翻訳処理開始 - チャンク数: {textChunks.Count}");
            
            // 翻訳サービスの詳細情報をログ出力
            var serviceType = _translationService.GetType().Name;
            DebugLogUtility.WriteLog($"🔧 使用中の翻訳サービス: {serviceType}");
            
            // 🚀 Phase 2: バッチ翻訳の実装
            // 空でないテキストチャンクを抽出
            var nonEmptyChunks = textChunks.Where(c => !string.IsNullOrWhiteSpace(c.CombinedText)).ToList();
            var emptyChunks = textChunks.Where(c => string.IsNullOrWhiteSpace(c.CombinedText)).ToList();
            
            // 空のチャンクは翻訳をスキップ
            foreach (var emptyChunk in emptyChunks)
            {
                emptyChunk.TranslatedText = "";
            }
            
            if (nonEmptyChunks.Count > 0)
            {
                using var batchTranslationMeasurement = new PerformanceMeasurement(
                    MeasurementType.TranslationProcessing, 
                    $"バッチ翻訳処理 - {nonEmptyChunks.Count}チャンク")
                    .WithAdditionalInfo($"Service:{serviceType}");
                
                // バッチ翻訳リクエストを作成
                var batchTexts = nonEmptyChunks.Select(c => c.CombinedText).ToList();
                
                try
                {
                    _logger?.LogInformation("🚀 [BATCH_PROCESSING] バッチ翻訳試行開始 - テキスト数: {Count}", batchTexts.Count);
                    
                    // バッチ翻訳を試行（未実装の場合は個別処理にフォールバック）
                    var batchResults = await TranslateBatchAsync(
                        batchTexts,
                        Language.Japanese,
                        Language.English,
                        cancellationToken).ConfigureAwait(false);
                    
                    // 結果をチャンクに反映
                    for (int i = 0; i < nonEmptyChunks.Count && i < batchResults.Count; i++)
                    {
                        nonEmptyChunks[i].TranslatedText = batchResults[i];
                        DebugLogUtility.WriteLog($"   [{nonEmptyChunks[i].ChunkId}] '{nonEmptyChunks[i].CombinedText}' → '{batchResults[i]}'");
                    }
                    
                    var batchResult = batchTranslationMeasurement.Complete();
                    _logger?.LogInformation("✅ バッチ翻訳完了: {Count}チャンク, {Duration}ms", 
                        nonEmptyChunks.Count, batchResult.Duration.TotalMilliseconds);
                }
                catch (NotImplementedException)
                {
                    // バッチ翻訳が未実装の場合は個別処理にフォールバック
                    _logger?.LogWarning("⚠️ バッチ翻訳未実装のため個別処理にフォールバック");
                    
                    foreach (var chunk in nonEmptyChunks)
                    {
                        try
                        {
                            using var chunkTranslationMeasurement = new PerformanceMeasurement(
                                MeasurementType.TranslationProcessing, 
                                $"チャンク翻訳処理 - ChunkId:{chunk.ChunkId}, テキスト:'{chunk.CombinedText}' ({chunk.CombinedText.Length}文字)")
                                .WithAdditionalInfo($"Service:{serviceType}");
                                
                            var translationResult = await _translationService.TranslateAsync(
                                chunk.CombinedText, 
                                Language.Japanese, 
                                Language.English, 
                                null,
                                cancellationToken).ConfigureAwait(false);
                            
                            var chunkResult = chunkTranslationMeasurement.Complete();
                            
                            // 翻訳結果の詳細をログ出力
                            var engineName = translationResult.EngineName ?? "Unknown";
                            DebugLogUtility.WriteLog($"🔧 翻訳エンジン: {engineName}, 成功: {translationResult.IsSuccess}, 時間: {chunkResult.Duration.TotalMilliseconds:F1}ms");
                                
                            chunk.TranslatedText = translationResult.TranslatedText ?? string.Empty;
                            
                            _logger?.LogDebug("🌐 翻訳完了 - ChunkId: {ChunkId}, 原文: '{Original}', 翻訳: '{Translated}'", 
                                chunk.ChunkId, chunk.CombinedText, chunk.TranslatedText);
                            DebugLogUtility.WriteLog($"🌐 翻訳完了 - ChunkId: {chunk.ChunkId}, 原文: '{chunk.CombinedText}', 翻訳: '{chunk.TranslatedText}'");
                        }
                        catch (Exception ex)
                        {
                            // 翻訳エラー時はフォールバック
                            _logger?.LogWarning(ex, "⚠️ 翻訳エラー - ChunkId: {ChunkId}, フォールバック表示", chunk.ChunkId);
                            chunk.TranslatedText = $"[翻訳エラー] {chunk.CombinedText}";
                        }
                    }
                }
            }
            
            _logger?.LogInformation("✅ 翻訳処理完了 - 処理チャンク数: {Count}, 成功チャンク数: {SuccessCount}", 
                textChunks.Count, textChunks.Count(c => !string.IsNullOrEmpty(c.TranslatedText) && !c.TranslatedText.StartsWith("[翻訳エラー]", StringComparison.Ordinal)));

            // インプレースオーバーレイ表示を優先的に使用
            var inPlaceOverlayManager = _serviceProvider.GetService<IInPlaceTranslationOverlayManager>();
            if (inPlaceOverlayManager != null)
            {
                _logger?.LogInformation("🎯 インプレースオーバーレイ表示開始 - チャンク数: {Count}", textChunks.Count);
                DebugLogUtility.WriteLog($"🎯 インプレースオーバーレイ表示開始 - チャンク数: {textChunks.Count}");
                
                try
                {
                    // インプレース翻訳オーバーレイマネージャーを初期化
                    await inPlaceOverlayManager.InitializeAsync().ConfigureAwait(false);
                    
                    // 各テキストチャンクをインプレースで表示
                    DebugLogUtility.WriteLog($"\n🎭 インプレース表示開始処理:");
                    foreach (var chunk in textChunks)
                    {
                        DebugLogUtility.WriteLog($"\n🔸 チャンク {chunk.ChunkId} インプレース表示判定:");
                        DebugLogUtility.WriteLog($"   インプレース表示可能: {chunk.CanShowInPlace()}");
                        DebugLogUtility.WriteLog($"   元座標: ({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y})");
                        DebugLogUtility.WriteLog($"   元サイズ: ({chunk.CombinedBounds.Width},{chunk.CombinedBounds.Height})");
                        
                        if (chunk.CanShowInPlace())
                        {
                            _logger?.LogDebug("🎭 インプレース表示 - ChunkId: {ChunkId}, 位置: ({X},{Y}), サイズ: ({W}x{H})", 
                                chunk.ChunkId, chunk.CombinedBounds.X, chunk.CombinedBounds.Y, 
                                chunk.CombinedBounds.Width, chunk.CombinedBounds.Height);
                            
                            using var overlayMeasurement = new PerformanceMeasurement(
                                MeasurementType.OverlayRendering, 
                                $"インプレース表示 - ChunkId:{chunk.ChunkId}, 位置:({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y})")
                                .WithAdditionalInfo($"Text:'{chunk.TranslatedText}'");
                            
                            await inPlaceOverlayManager!.ShowInPlaceOverlayAsync(chunk, cancellationToken)
                                .ConfigureAwait(false);
                                
                            var overlayResult = overlayMeasurement.Complete();
                            
                            DebugLogUtility.WriteLog($"   ✅ インプレース表示完了 - チャンク {chunk.ChunkId}, 時間: {overlayResult.Duration.TotalMilliseconds:F1}ms");
                        }
                        else
                        {
                            _logger?.LogWarning("⚠️ インプレース表示条件を満たしていません - {InPlaceLog}", chunk.ToInPlaceLogString());
                            DebugLogUtility.WriteLog($"   ❌ インプレース表示スキップ - チャンク {chunk.ChunkId}: 条件未満足");
                        }
                    }
                    
                    _logger?.LogInformation("✅ インプレースオーバーレイ表示完了 - アクティブオーバーレイ数: {Count}", 
                        inPlaceOverlayManager!.ActiveOverlayCount);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "❌ インプレースオーバーレイ表示でエラーが発生");
                    DebugLogUtility.WriteLog($"❌❌❌ インプレースオーバーレイエラー: {ex.GetType().Name} - {ex.Message}");
                    
                    // インプレースUIでエラーが発生した場合は従来のオーバーレイにフォールバック
                    _logger?.LogWarning("🔄 従来のオーバーレイ表示にフォールバック");
                    await DisplayInPlaceTranslationOverlay(textChunks, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                // インプレースオーバーレイが利用できない場合は従来のオーバーレイを使用
                _logger?.LogWarning("⚠️ インプレースオーバーレイが利用できません。従来のオーバーレイを使用");
                await DisplayInPlaceTranslationOverlay(textChunks, cancellationToken).ConfigureAwait(false);
            }
            
            _logger?.LogInformation("🎉 座標ベース翻訳処理完了 - 座標ベース翻訳表示成功");
            DebugLogUtility.WriteLog("🎉 座標ベース翻訳処理完了 - 座標ベース翻訳表示成功");
            
            // BaketaLogManagerで座標ベース翻訳フローのパフォーマンスログを記録
            try
            {
                var operationId = Guid.NewGuid().ToString("N")[..8];
                var processingEndTime = DateTime.Now;
                var processingStartTime = processingEndTime.Subtract(ocrProcessingTime);
                var totalProcessingTime = (processingEndTime - processingStartTime).TotalMilliseconds;
                
                var performanceLogEntry = new PerformanceLogEntry
                {
                    OperationId = operationId,
                    OperationName = "CoordinateBasedTranslation",
                    DurationMs = totalProcessingTime,
                    MemoryUsageBytes = GC.GetTotalMemory(false),
                    BottleneckAnalysis = new Dictionary<string, object>
                    {
                        ["ocrProcessingTimeMs"] = ocrProcessingTime.TotalMilliseconds,
                        ["textChunksProcessed"] = textChunks.Count,
                        ["imageSize"] = $"{image.Width}x{image.Height}",
                        ["windowHandle"] = $"0x{windowHandle.ToInt64():X}"
                    },
                    Metadata = new Dictionary<string, object>
                    {
                        ["mode"] = "coordinate_based_translation",
                        ["hasOverlay"] = true,
                        ["chunksTranslated"] = textChunks.Count(c => !string.IsNullOrEmpty(c.TranslatedText))
                    },
                    Level = totalProcessingTime > 5000 ? PerformanceLevel.Critical 
                          : totalProcessingTime > 2000 ? PerformanceLevel.Warning 
                          : PerformanceLevel.Normal
                };
                
                BaketaLogManager.LogPerformance(performanceLogEntry);
            }
            catch (Exception logEx)
            {
                _logger?.LogWarning(logEx, "座標ベース翻訳のパフォーマンスログ記録に失敗");
            }
        }
        catch (TaskCanceledException)
        {
            _logger?.LogDebug("座標ベース翻訳処理がキャンセルされました");
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ 座標ベース翻訳処理でエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// バッチ翻訳を実行（TransformersOpusMtEngineバッチ処理による最適化）
    /// </summary>
    private async Task<List<string>> TranslateBatchAsync(
        List<string> texts,
        Language sourceLanguage,
        Language targetLanguage,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("🔍 [BATCH_DEBUG] TranslateBatchAsync呼び出し開始 - テキスト数: {Count}", texts.Count);
        // まず、ITranslationServiceがバッチ翻訳をサポートしているか確認
        if (_translationService is ITranslationServiceWithBatch batchService)
        {
            // バッチ翻訳をサポートしている場合は直接実行
            return await batchService.TranslateBatchAsync(
                texts, sourceLanguage, targetLanguage, cancellationToken)
                .ConfigureAwait(false);
        }

        // TranslationServiceからTransformersOpusMtEngineを取得してバッチ処理を試行
        if (TryGetTransformersOpusMtEngine(out var transformersEngine))
        {
            _logger?.LogInformation("🚀 [BATCH_PROCESSING] TransformersOpusMtEngineバッチ処理を使用");
            
            try
            {
                // バッチ翻訳リクエストを作成
                var requests = texts.Select(text => new TranslationRequest
                {
                    SourceText = text,
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage
                }).ToList();

                var responses = await transformersEngine.TranslateBatchAsync(requests, cancellationToken)
                    .ConfigureAwait(false);

                var batchResults = responses.Select(r => r.TranslatedText ?? "[Batch Translation Failed]").ToList();
                
                _logger?.LogInformation("✅ [BATCH_PROCESSING] TransformersOpusMtEngineバッチ処理成功 - 処理数: {Count}", batchResults.Count);
                return batchResults;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "⚠️ [BATCH_PROCESSING] TransformersOpusMtEngineバッチ処理失敗、個別処理にフォールバック");
            }
        }
        
        // 🔧 一時的に並列処理を無効化（TransformersOpusMtEngineのIOException問題調査のため）
        var results = new List<string>();
        
        _logger?.LogInformation("🔄 順次翻訳開始 - チャンク数: {Count}", texts.Count);
        
        foreach (var text in texts)
        {
            try
            {
                var result = await _translationService.TranslateAsync(
                    text, sourceLanguage, targetLanguage, null, cancellationToken)
                    .ConfigureAwait(false);
                results.Add(result.TranslatedText ?? "[Translation Failed]");
                
                _logger?.LogDebug("✅ 順次翻訳完了: {Text} → {Result}", 
                    text.Length > 20 ? text.Substring(0, 20) + "..." : text,
                    (result.TranslatedText ?? "[Translation Failed]").Length > 20 ? 
                        result.TranslatedText.Substring(0, 20) + "..." : result.TranslatedText ?? "[Translation Failed]");
            }
            catch (TaskCanceledException)
            {
                results.Add("[Translation Timeout]");
                _logger?.LogWarning("⚠️ 翻訳タイムアウト: {Text}", text.Length > 20 ? text.Substring(0, 20) + "..." : text);
            }
            catch (Exception ex)
            {
                results.Add("[Translation Failed]");
                _logger?.LogError(ex, "❌ 翻訳エラー: {Text}", text.Length > 20 ? text.Substring(0, 20) + "..." : text);
            }
        }
        
        _logger?.LogInformation("🏁 順次翻訳完了 - 成功: {Success}/{Total}", 
            results.Count(r => !r.StartsWith("[", StringComparison.Ordinal)), results.Count);
        
        return results;
    }

    /// <summary>
    /// TransformersOpusMtEngineの取得を試行
    /// </summary>
    private bool TryGetTransformersOpusMtEngine(out TransformersOpusMtEngine? engine)
    {
        engine = null;
        
        try
        {
            _logger?.LogInformation("🔍 [BATCH_DEBUG] TryGetTransformersOpusMtEngine開始 - _translationService型: {ServiceType}", _translationService.GetType().Name);
            
            // 直接TransformersOpusMtEngineのインスタンスかチェック
            if (_translationService is TransformersOpusMtEngine directEngine)
            {
                _logger?.LogInformation("✅ [BATCH_DEBUG] 直接キャストで取得成功");
                engine = directEngine;
                return true;
            }
            _logger?.LogInformation("❌ [BATCH_DEBUG] 直接キャスト失敗");

            // TranslationServiceから依存関係注入でTransformersOpusMtEngineを取得
            var transformersEngine = _serviceProvider.GetService<TransformersOpusMtEngine>();
            if (transformersEngine != null)
            {
                _logger?.LogInformation("✅ [BATCH_DEBUG] ServiceProvider経由で取得成功: {EngineType}", transformersEngine.GetType().Name);
                engine = transformersEngine;
                return true;
            }
            _logger?.LogInformation("❌ [BATCH_DEBUG] ServiceProvider経由での取得失敗");

            // リフレクションを使ってDefaultTranslationServiceから探索
            var serviceType = _translationService.GetType();
            _logger?.LogInformation("🔍 [BATCH_DEBUG] リフレクション探索開始 - 対象型: {ServiceType}", serviceType.Name);
            
            // DefaultTranslationServiceの_availableEnginesフィールドを確認
            var availableEnginesField = serviceType.GetField("_availableEngines", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (availableEnginesField != null)
            {
                _logger?.LogInformation("🔍 [BATCH_DEBUG] _availableEnginesフィールド発見");
                if (availableEnginesField.GetValue(_translationService) is IEnumerable<object> availableEngines)
                {
                    _logger?.LogInformation("🔍 [BATCH_DEBUG] _availableEnginesの中身を探索中...");
                    var engineList = availableEngines.ToList();
                    _logger?.LogInformation("🔍 [BATCH_DEBUG] _availableEnginesエンジン数: {Count}", engineList.Count);
                    
                    for (int i = 0; i < engineList.Count; i++)
                    {
                        var eng = engineList[i];
                        _logger?.LogInformation("🔍 [BATCH_DEBUG] エンジン[{Index}]: {EngineType}", i, eng?.GetType().Name);
                    }
                    
                    var transformersEngineFromList = engineList.OfType<TransformersOpusMtEngine>().FirstOrDefault();
                    if (transformersEngineFromList != null)
                    {
                        _logger?.LogInformation("✅ [BATCH_DEBUG] リフレクション経由で取得成功: {EngineType}", transformersEngineFromList.GetType().Name);
                        engine = transformersEngineFromList;
                        return true;
                    }
                }
            }
            else
            {
                _logger?.LogInformation("❌ [BATCH_DEBUG] _availableEnginesフィールドが見つかりません");
            }
            
            // 従来の_enginesフィールドも確認（CompositeTranslationService用）
            var enginesField = serviceType.GetField("_engines", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (enginesField?.GetValue(_translationService) is IEnumerable<object> engines)
            {
                _logger?.LogInformation("🔍 [BATCH_DEBUG] _enginesフィールドから探索成功");
                var transformersEngineFromList = engines.OfType<TransformersOpusMtEngine>().FirstOrDefault();
                if (transformersEngineFromList != null)
                {
                    _logger?.LogInformation("✅ [BATCH_DEBUG] _enginesフィールドから取得成功");
                    engine = transformersEngineFromList;
                    return true;
                }
            }

            _logger?.LogWarning("❌ [BATCH_DEBUG] TransformersOpusMtEngineが見つかりませんでした - サービス型: {ServiceType}", serviceType.Name);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "💥 [BATCH_DEBUG] TransformersOpusMtEngine取得中にエラー");
            return false;
        }
    }
    
    /// <summary>
    /// インプレース翻訳オーバーレイ表示
    /// </summary>
    private async Task DisplayInPlaceTranslationOverlay(
        IReadOnlyList<TextChunk> textChunks, 
        CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogDebug("🖼️ インプレース翻訳オーバーレイ表示開始");
            DebugLogUtility.WriteLog("🖼️ インプレース翻訳オーバーレイ表示開始");
            
            DebugLogUtility.WriteLog($"🔥🔥🔥 インプレース翻訳オーバーレイ表示直前 - _overlayManager null?: {_overlayManager == null}");
            if (_overlayManager != null)
            {
                // 各TextChunkを個別にインプレース表示
                foreach (var textChunk in textChunks)
                {
                    await _overlayManager.ShowInPlaceOverlayAsync(textChunk, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            DebugLogUtility.WriteLog("🔥🔥🔥 インプレース翻訳オーバーレイ表示完了");
        }
        catch (TaskCanceledException)
        {
            _logger?.LogDebug("インプレース翻訳オーバーレイ表示がキャンセルされました");
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ インプレース翻訳オーバーレイ表示でエラーが発生");
            DebugLogUtility.WriteLog($"❌❌❌ インプレース翻訳オーバーレイエラー: {ex.GetType().Name} - {ex.Message}");
            DebugLogUtility.WriteLog($"❌❌❌ スタックトレース: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// OCR完了イベントを発行する
    /// </summary>
    /// <param name="image">OCR処理元画像</param>
    /// <param name="textChunks">OCR結果のテキストチャンク</param>
    /// <param name="processingTime">OCR処理時間</param>
    private async Task PublishOcrCompletedEventAsync(IAdvancedImage image, IReadOnlyList<TextChunk> textChunks, TimeSpan processingTime)
    {
        Console.WriteLine($"🔥 [DEBUG] PublishOcrCompletedEventAsync呼び出し開始: チャンク数={textChunks.Count}");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] PublishOcrCompletedEventAsync呼び出し開始: チャンク数={textChunks.Count}{Environment.NewLine}");
        
        try
        {
            Console.WriteLine($"🔥 [DEBUG] SelectMany実行開始 - textChunks.Count={textChunks.Count}");
            var positionedResults = textChunks.SelectMany(chunk => chunk.TextResults).ToList();
            Console.WriteLine($"🔥 [DEBUG] SelectMany実行完了 - positionedResults作成成功");
            Console.WriteLine($"🔥 [DEBUG] TextResults検証: チャンク数={textChunks.Count}, positionedResults数={positionedResults.Count}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] TextResults検証: チャンク数={textChunks.Count}, positionedResults数={positionedResults.Count}{Environment.NewLine}");
            
            Console.WriteLine($"🔥 [DEBUG] 条件判定: positionedResults.Count={positionedResults.Count}, 条件結果={positionedResults.Count > 0}");
            if (positionedResults.Count > 0)
            {
                Console.WriteLine($"🔥 [DEBUG] OcrResult作成開始 - positionedResults数: {positionedResults.Count}");
                
                var ocrResults = positionedResults.Select(posResult => new OcrResult(
                    text: posResult.Text,
                    bounds: posResult.BoundingBox,
                    confidence: posResult.Confidence)).ToList();
                    
                Console.WriteLine($"🔥 [DEBUG] OcrResult作成完了 - ocrResults数: {ocrResults.Count}");
                
                var ocrCompletedEvent = new OcrCompletedEvent(
                    sourceImage: image,
                    results: ocrResults,
                    processingTime: processingTime);
                    
                Console.WriteLine($"🔥 [DEBUG] OcrCompletedEvent作成完了 - ID: {ocrCompletedEvent.Id}");
                    
                _logger?.LogDebug("🔥 OCR完了イベント発行開始 - Results: {ResultCount}", ocrResults.Count);
                Console.WriteLine($"🔥 [DEBUG] OCR完了イベント発行開始 - Results: {ocrResults.Count}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] OCR完了イベント発行開始 - Results: {ocrResults.Count}{Environment.NewLine}");
                
                try
                {
                    Console.WriteLine($"🔥 [DEBUG] EventAggregator.PublishAsync呼び出し直前");
                    Console.WriteLine($"🔥 [DEBUG] EventAggregator型: {_eventAggregator.GetType().FullName}");
                    Console.WriteLine($"🔥 [DEBUG] EventAggregatorハッシュ: {_eventAggregator.GetHashCode()}");
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] PublishAsync直前 - EventAggregator型: {_eventAggregator.GetType().FullName}, ハッシュ: {_eventAggregator.GetHashCode()}{Environment.NewLine}");
                    await _eventAggregator.PublishAsync(ocrCompletedEvent).ConfigureAwait(false);
                    Console.WriteLine($"🔥 [DEBUG] EventAggregator.PublishAsync呼び出し完了");
                }
                catch (Exception publishEx)
                {
                    Console.WriteLine($"🔥 [ERROR] EventAggregator.PublishAsync例外: {publishEx.GetType().Name} - {publishEx.Message}");
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [ERROR] EventAggregator.PublishAsync例外: {publishEx.GetType().Name} - {publishEx.Message}{Environment.NewLine}");
                    throw;
                }
                
                _logger?.LogDebug("🔥 OCR完了イベント発行完了 - Results: {ResultCount}", ocrResults.Count);
                Console.WriteLine($"🔥 [DEBUG] OCR完了イベント発行完了 - Results: {ocrResults.Count}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] OCR完了イベント発行完了 - Results: {ocrResults.Count}{Environment.NewLine}");
            }
            else
            {
                _logger?.LogInformation("📝 OCR結果が0件のため、OCR完了イベントの発行をスキップ");
                Console.WriteLine($"🔥 [DEBUG] OCR結果が0件のため、OCR完了イベントの発行をスキップ");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] OCR結果が0件のため、OCR完了イベントの発行をスキップ{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OCR完了イベントの発行に失敗しました");
            Console.WriteLine($"🔥 [ERROR] PublishOcrCompletedEventAsync例外: {ex.GetType().Name} - {ex.Message}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [ERROR] PublishOcrCompletedEventAsync例外: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}");
        }
    }

    /// <summary>
    /// 座標ベース翻訳システムが利用可能かどうかを確認
    /// </summary>
    public bool IsCoordinateBasedTranslationAvailable()
    {
        ThrowIfDisposed();
        
        try
        {
            var batchOcrAvailable = _batchOcrProcessor != null;
            var overlayAvailable = _overlayManager != null;
            var available = batchOcrAvailable && overlayAvailable;
            
            DebugLogUtility.WriteLog($"🔍 [CoordinateBasedTranslationService] 座標ベース翻訳システム可用性チェック:");
            DebugLogUtility.WriteLog($"   📦 BatchOcrProcessor: {batchOcrAvailable}");
            DebugLogUtility.WriteLog($"   🖼️ OverlayManager: {overlayAvailable}");
            DebugLogUtility.WriteLog($"   ✅ 総合判定: {available}");
            
            _logger?.LogDebug("🔍 座標ベース翻訳システム可用性チェック: {Available}", available);
            return available;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "⚠️ 座標ベース翻訳システム可用性チェックでエラー");
            return false;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            // MultiWindowOverlayManagerのクリーンアップ
            if (_overlayManager is IDisposable disposableOverlayManager)
            {
                disposableOverlayManager.Dispose();
            }

            // BatchOcrProcessorのクリーンアップ
            if (_batchOcrProcessor is IDisposable disposableBatchProcessor)
            {
                disposableBatchProcessor.Dispose();
            }

            _disposed = true;
            _logger?.LogInformation("🧹 CoordinateBasedTranslationService disposed - Hash: {Hash}", this.GetHashCode());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ CoordinateBasedTranslationService dispose error");
        }
    }
}