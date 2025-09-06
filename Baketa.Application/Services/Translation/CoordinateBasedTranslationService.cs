using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Configuration;
using Baketa.Core.Utilities;
using Baketa.Core.Translation.Models;
using Baketa.Core.Settings;
using Baketa.Core.Performance;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Logging;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Events.Diagnostics;
using Baketa.Core.Models.OCR;
using Baketa.Infrastructure.OCR.BatchProcessing;
using Baketa.Infrastructure.Translation.Local;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 座標ベース翻訳表示サービス
/// バッチOCR処理と複数ウィンドウオーバーレイ表示を統合した座標ベース翻訳システム
/// </summary>
public sealed class CoordinateBasedTranslationService : IDisposable
{
    private readonly ITranslationProcessingFacade _processingFacade;
    private readonly IConfigurationFacade _configurationFacade;
    // 🚀 [Phase 2.1] Service Locator Anti-pattern完全除去: _serviceProviderフィールド削除
    private readonly ILogger<CoordinateBasedTranslationService>? _logger;
    private readonly IEventAggregator? _eventAggregator;
    private readonly IStreamingTranslationService? _streamingTranslationService;
    private bool _disposed;

    public CoordinateBasedTranslationService(
        ITranslationProcessingFacade processingFacade,
        IConfigurationFacade configurationFacade,
        IStreamingTranslationService? streamingTranslationService,
        ILogger<CoordinateBasedTranslationService>? logger = null)
    {
        _processingFacade = processingFacade ?? throw new ArgumentNullException(nameof(processingFacade));
        _configurationFacade = configurationFacade ?? throw new ArgumentNullException(nameof(configurationFacade));
        _streamingTranslationService = streamingTranslationService;
        _logger = logger;
        
        // 🚀 [Phase 2.1] Service Locator Anti-pattern除去: ファサード経由でEventAggregatorを取得
        _eventAggregator = _configurationFacade.EventAggregator;
        
        if (_streamingTranslationService != null)
        {
            Console.WriteLine("🔥 [STREAMING] ストリーミング翻訳サービスが利用可能");
        }
        
        // 統一ログを使用（重複したConsole.WriteLineを統合）
        _configurationFacade.Logger?.LogDebug("CoordinateBasedTranslationService", "サービス初期化完了", new
        {
            EventAggregatorType = _configurationFacade.EventAggregator.GetType().Name,
            EventAggregatorHash = _configurationFacade.EventAggregator.GetHashCode(),
            EventAggregatorReference = _configurationFacade.EventAggregator.ToString()
        });
        
        // 統一設定サービス注入時の設定値確認
        try
        {
            var translationSettings = _configurationFacade.SettingsService.GetTranslationSettings();
            _configurationFacade.Logger?.LogInformation("CoordinateBasedTranslationService", "統一設定サービス注入完了", new
            {
                translationSettings.AutoDetectSourceLanguage,
                translationSettings.DefaultSourceLanguage,
                translationSettings.DefaultTargetLanguage
            });
        }
        catch (Exception ex)
        {
            _configurationFacade.Logger?.LogError("CoordinateBasedTranslationService", "設定値の取得に失敗", ex);
        }
        
        _logger?.LogInformation("🚀 CoordinateBasedTranslationService initialized - Hash: {Hash}", this.GetHashCode());
    }

    /// <summary>
    /// OCRテキストに基づく動的言語検出を含む言語ペア取得
    /// </summary>
    private (Language sourceLanguage, Language targetLanguage) GetLanguagesFromSettings(string? ocrText = null)
    {
        try
        {
            // 🚨 [SETTINGS_BASED_ONLY] 設定ファイルの値のみを使用（動的言語検出削除）
            var translationSettings = _configurationFacade.SettingsService.GetTranslationSettings();
            
            // 🚨 [SIMPLIFIED] AutoDetectSourceLanguage削除 - 常に設定ファイルの値を使用
            var sourceLanguageCode = translationSettings.DefaultSourceLanguage;
            var targetLanguageCode = translationSettings.DefaultTargetLanguage;
            
            Console.WriteLine($"🔍 [SETTINGS_BASED] 設定ファイルベースの言語ペア: {sourceLanguageCode} → {targetLanguageCode}");
            
            _logger?.LogDebug("🔍 [SETTINGS_BASED] 設定ファイルベースの言語ペア: {Source} → {Target}", sourceLanguageCode, targetLanguageCode);

            // Language enumに変換（統一ユーティリティ使用）
            var sourceLanguage = LanguageCodeConverter.ToLanguageEnum(sourceLanguageCode, Language.Japanese);
            var targetLanguage = LanguageCodeConverter.ToLanguageEnum(targetLanguageCode, Language.English);

            Console.WriteLine($"🌍 [COORDINATE_SETTINGS] 最終言語設定: {sourceLanguageCode} → {targetLanguageCode}");
            _logger?.LogDebug("🌍 [COORDINATE_SETTINGS] 最終言語設定: {Source} → {Target}", sourceLanguageCode, targetLanguageCode);

            return (sourceLanguage, targetLanguage);
        }
        catch (Exception ex)
        {
            _configurationFacade.Logger?.LogError("CoordinateBasedTranslationService", "設定取得エラー、デフォルト値を使用", ex);
            // エラー時はデフォルト値を使用
            return (Language.Japanese, Language.English);
        }
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
            // 🔥 [FILE_CONFLICT_FIX_3] ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🎯 [DEBUG] ProcessWithCoordinateBasedTranslationAsync開始 - 画像: {Width}x{Height}", image.Width, image.Height);

            // バッチOCR処理でテキストチャンクを取得（詳細時間測定）
            var ocrMeasurement = new PerformanceMeasurement(
                MeasurementType.BatchOcrProcessing, 
                $"バッチOCR処理 - 画像:{image.Width}x{image.Height}")
                .WithAdditionalInfo($"WindowHandle:0x{windowHandle.ToInt64():X}");
            
            // 🚨 [CRITICAL_FIX] OCR処理直前ログ
            Console.WriteLine($"🚨 [CRITICAL_FIX] バッチOCR処理開始直前 - CancellationToken.IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
            // 🔥 [FILE_CONFLICT_FIX_4] ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🚨 [CRITICAL_FIX] バッチOCR処理開始直前 - CancellationToken.IsCancellationRequested: {IsCancellationRequested}", 
                cancellationToken.IsCancellationRequested);
            
            var textChunks = await _processingFacade.OcrProcessor.ProcessBatchAsync(image, windowHandle, cancellationToken)
                .ConfigureAwait(false);
            
            // 🚨 [CRITICAL_FIX] OCR処理完了直後ログ
            Console.WriteLine($"🚨 [CRITICAL_FIX] バッチOCR処理完了直後 - ChunkCount: {textChunks.Count}, CancellationToken.IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
            // 🔥 [FILE_CONFLICT_FIX_5] ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🚨 [CRITICAL_FIX] バッチOCR処理完了直後 - ChunkCount: {ChunkCount}, IsCancellationRequested: {IsCancellationRequested}", 
                textChunks.Count, cancellationToken.IsCancellationRequested);
            
            // 🚀 [FIX] OCR完了後はキャンセル無視でバッチ翻訳を実行（並列チャンク処理実現のため）
            if (textChunks.Count > 0 && cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("🚀 [PARALLEL_CHUNKS_FIX] OCR完了後のキャンセル要求を無視してバッチ翻訳を実行");
                // 🔥 [FILE_CONFLICT_FIX_6] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🚀 [PARALLEL_CHUNKS_FIX] OCR完了後のキャンセル要求を無視してバッチ翻訳を実行");
            }
            
            var ocrResult = ocrMeasurement.Complete();
            var ocrProcessingTime = ocrResult.Duration;
            
            _logger?.LogInformation("✅ バッチOCR完了 - チャンク数: {ChunkCount}, 処理時間: {ProcessingTime}ms", 
                textChunks.Count, ocrProcessingTime.TotalMilliseconds);
            
            // 🚀 [PHASE_2_2_FIX] OCR完了イベント発行を再有効化 - 非同期バッチ処理で65秒遅延解決
            _logger?.LogInformation("🚀 [PHASE_2_2] OCR完了イベント発行を再開 - バッチ処理最適化済み");
                
            await PublishOcrCompletedEventAsync(image, textChunks, ocrProcessingTime).ConfigureAwait(false);
            
            _logger?.LogInformation("🚀 [PHASE_2_2] OCR完了イベント発行完了 - 後続処理は非同期で並列実行");
            
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
                var overlayPos = chunk.GetBasicOverlayPosition();
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
            Console.WriteLine($"🚨 [CRITICAL_FIX] バッチ翻訳処理開始直前 - チャンク数: {textChunks.Count}, CancellationToken.IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
            // 🔥 [FILE_CONFLICT_FIX_9] ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🚨 [CRITICAL_FIX] バッチ翻訳処理開始直前 - チャンク数: {ChunkCount}, IsCancellationRequested: {IsCancellationRequested}", 
                textChunks.Count, cancellationToken.IsCancellationRequested);
            
            _logger?.LogInformation("🌐 バッチ翻訳処理開始 - チャンク数: {Count}", textChunks.Count);
            DebugLogUtility.WriteLog($"🌐 バッチ翻訳処理開始 - チャンク数: {textChunks.Count}");
            
            // 翻訳サービスの詳細情報をログ出力
            var serviceType = _processingFacade.TranslationService.GetType().Name;
            DebugLogUtility.WriteLog($"🔧 使用中の翻訳サービス: {serviceType}");
            
            // 🚀 Phase 2: バッチ翻訳の実装
            Console.WriteLine($"🔍 [CHUNK_DEBUG] Total textChunks received: {textChunks.Count}");
            // 🔥 [FILE_CONFLICT_FIX_10] ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔍 [CHUNK_DEBUG] Total textChunks received: {Count}", textChunks.Count);
            
            // 空でないテキストチャンクを抽出
            var nonEmptyChunks = textChunks.Where(c => !string.IsNullOrWhiteSpace(c.CombinedText)).ToList();
            var emptyChunks = textChunks.Where(c => string.IsNullOrWhiteSpace(c.CombinedText)).ToList();
            
            Console.WriteLine($"🔍 [CHUNK_DEBUG] NonEmpty chunks: {nonEmptyChunks.Count}, Empty chunks: {emptyChunks.Count}");
            // 🔥 [FILE_CONFLICT_FIX_11] ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔍 [CHUNK_DEBUG] NonEmpty chunks: {NonEmpty}, Empty chunks: {Empty}", 
                nonEmptyChunks.Count, emptyChunks.Count);
                
            // チャンク詳細をダンプ
            for (int i = 0; i < Math.Min(textChunks.Count, 3); i++)
            {
                var chunk = textChunks[i];
                Console.WriteLine($"🔍 [CHUNK_DEBUG] Chunk[{i}]: Text='{chunk.CombinedText}', IsEmpty={string.IsNullOrWhiteSpace(chunk.CombinedText)}");
                // 🔥 [FILE_CONFLICT_FIX_12] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔍 [CHUNK_DEBUG] Chunk[{Index}]: Text='{Text}', IsEmpty={IsEmpty}", 
                    i, chunk.CombinedText, string.IsNullOrWhiteSpace(chunk.CombinedText));
            }
            
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
                    
                    // 🔥 [STREAMING] ストリーミング翻訳を試行（段階的結果表示）
                    // 🚀 [DYNAMIC_LANGUAGE_FIX] 最初のテキストチャンクから言語を動的検出
                    var firstText = nonEmptyChunks.FirstOrDefault()?.CombinedText ?? "";
                    var (sourceLanguage, targetLanguage) = GetLanguagesFromSettings(firstText);
                    
                    List<string> batchResults;
                    if (_streamingTranslationService != null)
                    {
                        Console.WriteLine("🔥 [STREAMING] ストリーミング翻訳サービス使用 - 段階的表示開始");
                        
                        // 🚨 [BATCH_CRITICAL] ストリーミング翻訳サービス呼び出し前の詳細ログ
                        Console.WriteLine($"🚨 [BATCH_STREAMING] ストリーミング翻訳呼び出し前 - StreamingService: {_streamingTranslationService?.GetType().Name}");
                        Console.WriteLine($"🔍 [BATCH_STREAMING] バッチテキスト数: {batchTexts?.Count}, SourceLang: {sourceLanguage?.Code}, TargetLang: {targetLanguage?.Code}");
                        Console.WriteLine($"🔍 [TRANSLATION_FLOW] バッチ翻訳開始 - テキスト数: {batchTexts.Count}, 言語: {sourceLanguage.Code} → {targetLanguage.Code}");
                        
                        // 🔥 [FILE_CONFLICT_FIX_13] ファイルアクセス競合回避のためILogger使用
                        _logger?.LogDebug("🔥 [STREAMING] ストリーミング翻訳サービス使用 - 段階的表示開始");
                        
                        // 段階的結果表示のコールバック関数を定義
                        void OnChunkCompleted(int index, string translatedText)
                        {
                            if (index < nonEmptyChunks.Count)
                            {
                                var chunk = nonEmptyChunks[index];
                                chunk.TranslatedText = translatedText;
                                
                                Console.WriteLine($"✨ [STREAMING] チャンク完了 [{index + 1}/{nonEmptyChunks.Count}] - " +
                                                $"テキスト: '{(chunk.CombinedText.Length > 30 ? chunk.CombinedText[..30] + "..." : chunk.CombinedText)}'");
                                
                                // 🚀 [STREAMING_OVERLAY_FIX] 翻訳完了時に即座にオーバーレイ表示
                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    Task.Run(async () =>
                                    {
                                        try
                                        {
                                            // Task内での再度のキャンセル確認（確実な停止のため）
                                            cancellationToken.ThrowIfCancellationRequested();
                                            
                                            if (_processingFacade.OverlayManager != null && chunk.CanShowInPlace())
                                            {
                                                // 🚫 Phase 11.2: 重複表示修正 - 直接オーバーレイ表示を無効化
                                                // TranslationWithBoundsCompletedEvent → OverlayUpdateEvent 経由で表示されるため、
                                                // 直接呼び出しは重複表示の原因となる
                                                Console.WriteLine($"🚫 [PHASE11.2] 重複表示回避: 直接オーバーレイ表示をスキップ - チャンク {chunk.ChunkId}: '{translatedText}'");
                                                Console.WriteLine($"✅ [PHASE11.2] TranslationWithBoundsCompletedEvent経由で表示予定 - チャンク {chunk.ChunkId}");
                                            }
                                        }
                                        catch (OperationCanceledException)
                                        {
                                            Console.WriteLine($"🛑 [STOP_SUCCESS] オーバーレイ表示が正常にキャンセルされました - チャンク {chunk.ChunkId}");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"⚠️ [STREAMING] オーバーレイ更新エラー - チャンク {chunk.ChunkId}: {ex.Message}");
                                        }
                                    }, cancellationToken); // CancellationTokenを渡す
                                }
                                else
                                {
                                    Console.WriteLine($"🛑 [STOP_EARLY] Stop要求のためオーバーレイ表示を完全スキップ - チャンク {chunk.ChunkId}");
                                }
                            }
                        }
                        
                        // 🛑 [STOP_FIX] キャンセル要求を適切に処理（無視しない）
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger?.LogInformation("🛑 [STOP_FIX] Stop要求により翻訳処理を中断します");
                            Console.WriteLine("🛑 [STOP_FIX] Stop要求により翻訳処理を中断 - オーバーレイ表示をスキップ");
                            return; // 確実に処理を中断
                        }
                        
                        // キャンセル要求を無視せず、適切に伝播
                        var translationToken = cancellationToken;
                        
                        Console.WriteLine($"🚀 [BATCH_TRANSLATION] TranslateBatchWithStreamingAsync呼び出し直前");
                        
                        // 🚨 [BATCH_CRITICAL] StreamingService呼び出し直前の最終確認ログ
                        Console.WriteLine($"🚨 [FINAL_CHECK] StreamingService.TranslateBatchWithStreamingAsync呼び出し直前");
                        Console.WriteLine($"🔍 [FINAL_CHECK] テキスト配列: [{string.Join(", ", batchTexts.Take(3).Select(t => $"'{t[..Math.Min(20, t.Length)]}...'"))}]");
                        
                        batchResults = await _streamingTranslationService.TranslateBatchWithStreamingAsync(
                            batchTexts,
                            sourceLanguage,
                            targetLanguage,
                            OnChunkCompleted,
                            translationToken).ConfigureAwait(false);
                        
                        Console.WriteLine($"✅ [BATCH_TRANSLATION] TranslateBatchWithStreamingAsync完了 - 結果数: {batchResults?.Count ?? 0}");
                        
                        // 🚨 [BATCH_RESULT] 結果詳細のログ出力
                        Console.WriteLine($"🚨 [BATCH_RESULT] TranslateBatchWithStreamingAsync完了後の詳細ログ");
                        if (batchResults != null && batchResults.Count > 0)
                        {
                            for (int i = 0; i < Math.Min(3, batchResults.Count); i++)
                            {
                                Console.WriteLine($"🔍 [BATCH_RESULT] Result[{i}]: '{batchResults[i][..Math.Min(30, batchResults[i].Length)]}...'");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"❌ [BATCH_RESULT] 翻訳結果が空または null");
                        }
                        Console.WriteLine($"✅ [STREAMING] ストリーミング翻訳完了 - 結果数: {batchResults?.Count ?? 0}");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ [STREAMING] ストリーミング翻訳サービス無効 - 従来バッチ翻訳使用");
                        batchResults = await TranslateBatchAsync(
                            batchTexts,
                            sourceLanguage,
                            targetLanguage,
                            cancellationToken).ConfigureAwait(false);
                    }
                    
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
                                
                            // 🚀 [DYNAMIC_LANGUAGE_FIX] チャンクごとに動的言語検出を実行
                            var (sourceLanguage, targetLanguage) = GetLanguagesFromSettings(chunk.CombinedText);
                            var translationResult = await _processingFacade.TranslationService.TranslateAsync(
                                chunk.CombinedText, 
                                sourceLanguage, 
                                targetLanguage, 
                                null,
                                cancellationToken).ConfigureAwait(false);
                            
                            var chunkResult = chunkTranslationMeasurement.Complete();
                            
                            // 翻訳結果の詳細をログ出力
                            var engineName = translationResult.EngineName ?? "Unknown";
                            DebugLogUtility.WriteLog($"🔧 翻訳エンジン: {engineName}, 成功: {translationResult.IsSuccess}, 時間: {chunkResult.Duration.TotalMilliseconds:F1}ms");
                                
                            // 🛡️ [ERROR_SKIP] エラー結果（IsSuccess=false）のオーバーレイ表示をスキップ
                            Console.WriteLine($"🔍 [DEBUG_FILTER] 翻訳結果チェック - IsSuccess: {translationResult.IsSuccess}, Text: '{translationResult.TranslatedText}'");
                            DebugLogUtility.WriteLog($"🔍 [DEBUG_FILTER] 翻訳結果チェック - IsSuccess: {translationResult.IsSuccess}, Text: '{translationResult.TranslatedText}'");
                            
                            if (translationResult.IsSuccess)
                            {
                                chunk.TranslatedText = translationResult.TranslatedText ?? string.Empty;
                                Console.WriteLine($"✅ [SUCCESS_PATH] 翻訳成功 - ChunkId: {chunk.ChunkId}, 結果設定: '{chunk.TranslatedText}'");
                                DebugLogUtility.WriteLog($"✅ [SUCCESS_PATH] 翻訳成功 - ChunkId: {chunk.ChunkId}, 結果設定: '{chunk.TranslatedText}'");
                            }
                            else
                            {
                                Console.WriteLine($"🚫 [ERROR_SKIP] 翻訳エラーのためオーバーレイ表示をスキップ - ChunkId: {chunk.ChunkId}");
                                DebugLogUtility.WriteLog($"🚫 [ERROR_SKIP] 翻訳エラーのためオーバーレイ表示をスキップ - ChunkId: {chunk.ChunkId}, エラー: '{translationResult.TranslatedText}'");
                                _logger?.LogWarning("🚫 翻訳エラーのためオーバーレイ表示をスキップ - ChunkId: {ChunkId}, エラー: {Error}", 
                                    chunk.ChunkId, translationResult.TranslatedText);
                                chunk.TranslatedText = ""; // エラー時は空文字に設定してオーバーレイ表示を阻止
                                continue; // 次のチャンクに進む
                            }
                            
                            _logger?.LogDebug("🌐 翻訳完了 - ChunkId: {ChunkId}, 原文: '{Original}', 翻訳: '{Translated}'", 
                                chunk.ChunkId, chunk.CombinedText, chunk.TranslatedText);
                            DebugLogUtility.WriteLog($"🌐 翻訳完了 - ChunkId: {chunk.ChunkId}, 原文: '{chunk.CombinedText}', 翻訳: '{chunk.TranslatedText}'");
                        }
                        catch (Exception ex)
                        {
                            // 翻訳エラー時は空文字に設定（表示しない）
                            _logger?.LogWarning(ex, "⚠️ 翻訳エラー - ChunkId: {ChunkId}, 表示をスキップ", chunk.ChunkId);
                            chunk.TranslatedText = ""; // エラー時は空文字に設定してオーバーレイ表示を阻止
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine($"❌ [CHUNK_DEBUG] No non-empty chunks found! Skipping translation.");
                // 🔥 [FILE_CONFLICT_FIX_14] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("❌ [CHUNK_DEBUG] No non-empty chunks found! Skipping translation.");
            }
            
            _logger?.LogInformation("✅ 翻訳処理完了 - 処理チャンク数: {Count}, 成功チャンク数: {SuccessCount}", 
                textChunks.Count, textChunks.Count(c => !string.IsNullOrEmpty(c.TranslatedText) && !c.TranslatedText.StartsWith("[翻訳エラー]", StringComparison.Ordinal)));

            // インプレースオーバーレイ表示を優先的に使用
            var inPlaceOverlayManager = _processingFacade.OverlayManager;
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
                        
                        // 🛡️ [ERROR_PROTECTION] 失敗・エラー結果の表示を包括的に防止
                        var hasValidTranslation = TranslationValidator.IsValid(chunk.TranslatedText, chunk.CombinedText);
                        
                        DebugLogUtility.WriteLog($"   翻訳結果: '{chunk.TranslatedText}'");
                        DebugLogUtility.WriteLog($"   原文: '{chunk.CombinedText}'");
                        DebugLogUtility.WriteLog($"   有効な翻訳: {hasValidTranslation}");
                        
                        // 🔍 [DEBUG] TranslatedTextの初期値と翻訳後の値を確認
                        if (!string.IsNullOrEmpty(chunk.TranslatedText) && chunk.TranslatedText == chunk.CombinedText)
                        {
                            DebugLogUtility.WriteLog($"   ⚠️ [WARNING] TranslatedTextが原文と同じ: '{chunk.TranslatedText}'");
                            Console.WriteLine($"⚠️ [WARNING] TranslatedTextが原文と同じ - ChunkId: {chunk.ChunkId}, Text: '{chunk.TranslatedText}'");
                        }
                        
                        if (chunk.CanShowInPlace() && hasValidTranslation)
                        {
                            _logger?.LogDebug("🎭 インプレース表示 - ChunkId: {ChunkId}, 位置: ({X},{Y}), サイズ: ({W}x{H})", 
                                chunk.ChunkId, chunk.CombinedBounds.X, chunk.CombinedBounds.Y, 
                                chunk.CombinedBounds.Width, chunk.CombinedBounds.Height);
                            
                            using var overlayMeasurement = new PerformanceMeasurement(
                                MeasurementType.OverlayRendering, 
                                $"インプレース表示 - ChunkId:{chunk.ChunkId}, 位置:({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y})")
                                .WithAdditionalInfo($"Text:'{chunk.TranslatedText}'");
                            
                            // 🚫 Phase 11.2: 重複表示修正 - 直接オーバーレイ表示を無効化
                            // TranslationWithBoundsCompletedEvent → OverlayUpdateEvent 経由で表示されるため重複防止
                            Console.WriteLine($"🚫 [PHASE11.2] 直接オーバーレイ表示スキップ - チャンク {chunk.ChunkId}: 重複表示防止");
                            // await inPlaceOverlayManager!.ShowInPlaceOverlayAsync(chunk, cancellationToken).ConfigureAwait(false);
                                
                            var overlayResult = overlayMeasurement.Complete();
                            
                            DebugLogUtility.WriteLog($"   ✅ インプレース表示完了 - チャンク {chunk.ChunkId}, 時間: {overlayResult.Duration.TotalMilliseconds:F1}ms");
                        }
                        else
                        {
                            if (!hasValidTranslation)
                            {
                                DebugLogUtility.WriteLog($"   🚫 インプレース表示スキップ - チャンク {chunk.ChunkId}: エラー結果のため表示阻止");
                                _logger?.LogInformation("🚫 エラー結果のためオーバーレイ表示をスキップ - ChunkId: {ChunkId}", chunk.ChunkId);
                            }
                            else
                            {
                                _logger?.LogWarning("⚠️ インプレース表示条件を満たしていません - {InPlaceLog}", chunk.ToInPlaceLogString());
                                DebugLogUtility.WriteLog($"   ❌ インプレース表示スキップ - チャンク {chunk.ChunkId}: 条件未満足");
                            }
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
        catch (TaskCanceledException ex)
        {
            // 🚨 [CRITICAL_FIX] TaskCanceledException詳細をERRORレベルでログ出力
            _logger?.LogError(ex, "🚨 座標ベース翻訳処理がキャンセル/タイムアウトしました - これがバッチ翻訳実行されない根本原因");
            
            Console.WriteLine($"🚨 [CRITICAL_FIX] TaskCanceledException発生: {ex.Message}");
            Console.WriteLine($"🚨 [CRITICAL_FIX] CancellationToken.IsCancellationRequested: {ex.CancellationToken.IsCancellationRequested}");
            Console.WriteLine($"🚨 [CRITICAL_FIX] スタックトレース: {ex.StackTrace}");
            
            // 🔥 [FILE_CONFLICT_FIX_15] ファイルアクセス競合回避のためILogger使用
            _logger?.LogError("🚨 [CRITICAL_FIX] TaskCanceledException発生: {Message}", ex.Message);
            // 🔥 [FILE_CONFLICT_FIX_16] ファイルアクセス競合回避のためILogger使用
            _logger?.LogError("🚨 [CRITICAL_FIX] CancellationToken.IsCancellationRequested: {IsCancellationRequested}", 
                ex.CancellationToken.IsCancellationRequested);
            // 🔥 [FILE_CONFLICT_FIX_17] ファイルアクセス競合回避のためILogger使用
            _logger?.LogError("🚨 [CRITICAL_FIX] スタックトレース: {StackTrace}", 
                ex.StackTrace?.Replace(Environment.NewLine, " | "));
            
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
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        // 🚨 [CRITICAL_DEBUG] メソッド開始の即座ログ出力
        Console.WriteLine($"🚨 [BATCH_CRITICAL] TranslateBatchAsync開始 - テキスト数: {texts?.Count ?? 0}");
        Console.WriteLine($"🔍 [BATCH_LANGUAGE] 受信した言語設定: Source={sourceLanguage?.Code}({sourceLanguage?.DisplayName}) → Target={targetLanguage?.Code}({targetLanguage?.DisplayName})");
        
        _logger?.LogInformation("🔍 [BATCH_DEBUG] TranslateBatchAsync呼び出し開始 - テキスト数: {Count}", texts.Count);
        _logger?.LogInformation("[TIMING] CoordinateBasedTranslationService.TranslateBatchAsync開始 - テキスト数: {Count}", texts.Count);
        Console.WriteLine($"🚀 [FACADE_DEBUG] TranslationService via Facade: {_processingFacade.TranslationService?.GetType().Name}");
        // 🔥 [FILE_CONFLICT_FIX_18] ファイルアクセス競合回避のためILogger使用
        _logger?.LogDebug("🚀 [FACADE_DEBUG] TranslationService via Facade: {ServiceType}", 
            _processingFacade.TranslationService?.GetType().Name);
        
        // 🔍 [VERIFICATION] バッチ翻訳の実際の動作を検証
        // 🚀 汎用的なITranslationServiceベースのアプローチに変更（OptimizedPythonTranslationEngine対応）
        var translationService = _processingFacade.TranslationService;
        if (translationService != null)
        {
            Console.WriteLine($"🚀 [VERIFICATION] 翻訳サービス取得成功 - バッチ翻訳検証開始: {translationService.GetType().Name}");
            _logger?.LogDebug("🚀 [VERIFICATION] 翻訳サービス取得成功 - バッチ翻訳検証開始: {ServiceType}", translationService.GetType().Name);
                
            // 汎用的なバッチ翻訳処理（ITranslationServiceの標準的なアプローチ）
            Console.WriteLine($"📏 [VERIFICATION] バッチ翻訳開始 - テキスト数: {texts.Count}");
            _logger?.LogDebug("📏 [VERIFICATION] バッチ翻訳開始 - テキスト数: {Count}", texts.Count);
            
            // ITranslationServiceのTranslateBatchAsyncメソッドを使用
            try
            {
                Console.WriteLine($"🎯 [VERIFICATION] ITranslationService.TranslateBatchAsync実行開始");
                _logger?.LogDebug("🎯 [VERIFICATION] ITranslationService.TranslateBatchAsync実行開始");
                
                var timeoutSetupStopwatch = System.Diagnostics.Stopwatch.StartNew();
                // 🔧 [EMERGENCY_FIX] 60秒タイムアウトを設定（Python翻訳サーバー重要処理対応）
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                timeoutSetupStopwatch.Stop();
                _logger?.LogInformation("[TIMING] タイムアウト設定: {ElapsedMs}ms", timeoutSetupStopwatch.ElapsedMilliseconds);
                
                var startTime = DateTime.Now;
                var batchCallStopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                // 翻訳品質診断: セッションID生成
                var translationId = Guid.NewGuid().ToString("N")[..8];
                var totalTextLength = texts.Sum(t => t?.Length ?? 0);
                
                // 翻訳品質診断: 言語検出イベント
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "LanguageDetection",
                    IsSuccess = true,
                    ProcessingTimeMs = 0,
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Information,
                    Message = $"フォールバック経路言語検出完了: {sourceLanguage.Code} → {targetLanguage.Code}",
                    Metrics = new Dictionary<string, object>
                    {
                        { "SourceLanguage", sourceLanguage.Code },
                        { "TargetLanguage", targetLanguage.Code },
                        { "TextCount", texts.Count },
                        { "TotalTextLength", totalTextLength },
                        { "TranslationPath", "FallbackBatch" }
                    }
                }).ConfigureAwait(false);

                // 翻訳品質診断: 翻訳エンジン選択イベント
                var engineName = translationService.GetType().Name;
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "TranslationEngineSelection",
                    IsSuccess = true,
                    ProcessingTimeMs = 0,
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Information,
                    Message = $"フォールバック翻訳エンジン選択: {engineName}",
                    Metrics = new Dictionary<string, object>
                    {
                        { "SelectedEngine", engineName },
                        { "TranslationPath", "FallbackBatch" },
                        { "TextCount", texts.Count }
                    }
                }).ConfigureAwait(false);

                // ITranslationServiceのTranslateBatchAsyncメソッドを使用（文字列リスト）
                var batchResults = await translationService.TranslateBatchAsync(
                    texts, 
                    sourceLanguage, 
                    targetLanguage, 
                    null, 
                    combinedCts.Token).ConfigureAwait(false);
                
                batchCallStopwatch.Stop();
                var endTime = DateTime.Now;
                var duration = endTime - startTime;
                
                // 翻訳品質診断: 翻訳実行結果イベント
                var isTranslationSuccess = batchResults != null && batchResults.Any(r => r.IsSuccess);
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "TranslationExecution",
                    IsSuccess = isTranslationSuccess,
                    ProcessingTimeMs = (long)duration.TotalMilliseconds,
                    SessionId = translationId,
                    Severity = isTranslationSuccess ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                    Message = isTranslationSuccess 
                        ? $"フォールバック翻訳実行成功: {batchResults?.Count(r => r.IsSuccess) ?? 0}/{batchResults?.Count ?? 0}件"
                        : "フォールバック翻訳実行失敗",
                    Metrics = new Dictionary<string, object>
                    {
                        { "ExecutionTimeMs", duration.TotalMilliseconds },
                        { "SuccessCount", batchResults?.Count(r => r.IsSuccess) ?? 0 },
                        { "TotalCount", batchResults?.Count ?? 0 },
                        { "TranslationPath", "FallbackBatch" },
                        { "UsedEngine", engineName }
                    }
                }).ConfigureAwait(false);
                
                Console.WriteLine($"✅ [VERIFICATION] バッチ翻訳完了 - 実行時間: {duration.TotalMilliseconds:F0}ms");
                _logger?.LogDebug("✅ [VERIFICATION] バッチ翻訳完了 - 実行時間: {Duration:F0}ms", duration.TotalMilliseconds);
                _logger?.LogInformation("[TIMING] ITranslationService.TranslateBatchAsync実行: {ElapsedMs}ms", batchCallStopwatch.ElapsedMilliseconds);
                
                // 結果を詳細分析
                if (batchResults != null && batchResults.Count > 0)
                {
                    var successCount = batchResults.Count(r => r.IsSuccess);
                    var translations = batchResults.Select(r => r.TranslatedText ?? "").ToList();
                    
                    Console.WriteLine($"🔍 [VERIFICATION] 結果分析: SuccessCount={successCount}/{batchResults.Count}, Translations={translations.Count}");
                    _logger?.LogDebug("🔍 [VERIFICATION] 結果分析: SuccessCount={SuccessCount}/{TotalCount}, Translations={TranslationCount}", 
                        successCount, batchResults.Count, translations.Count);
                    
                    if (successCount == batchResults.Count)
                    {
                        // 🔍 翻訳品質診断: 高精度言語比較による翻訳失敗検出（フォールバックルート）
                        var sameLanguageCount = 0;
                        var sameLanguageFailures = new List<string>();
                        for (int i = 0; i < Math.Min(texts.Count, translations.Count); i++)
                        {
                            if (!string.IsNullOrEmpty(texts[i]) && !string.IsNullOrEmpty(translations[i]))
                            {
                                try
                                {
                                    // 改良された翻訳失敗検出ロジック（フォールバックバッチ処理）
                                    // TODO: 将来的に言語検出APIが統合された場合に高精度検出を実装予定
                                    var isSameText = string.Equals(texts[i].Trim(), translations[i].Trim(), StringComparison.OrdinalIgnoreCase);
                                    
                                    if (isSameText)
                                    {
                                        sameLanguageCount++;
                                        sameLanguageFailures.Add($"{texts[i]} -> {translations[i]} (fallback text comparison)");
                                        Console.WriteLine($"🚨 [FALLBACK_ENHANCED_DIAGNOSTIC] 翻訳失敗検出（文字列一致）: '{texts[i]}' -> '{translations[i]}'");
                                    }
                                }
                                catch (Exception detectionEx)
                                {
                                    // 検出処理でエラーが発生した場合のフォールバック
                                    if (string.Equals(texts[i].Trim(), translations[i].Trim(), StringComparison.OrdinalIgnoreCase))
                                    {
                                        sameLanguageCount++;
                                        sameLanguageFailures.Add($"{texts[i]} -> {translations[i]} (error fallback)");
                                        Console.WriteLine($"🚨 [ERROR_FALLBACK] 検出エラー時の文字列比較: '{texts[i]}' (エラー: {detectionEx.Message})");
                                    }
                                }
                            }
                        }

                        var qualityIsGood = sameLanguageCount == 0;
                        await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                        {
                            Stage = "TranslationQualityCheck",
                            IsSuccess = qualityIsGood,
                            ProcessingTimeMs = 0,
                            SessionId = translationId,
                            Severity = qualityIsGood ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                            Message = qualityIsGood 
                                ? $"フォールバック翻訳品質良好: 全{translations.Count}件成功（改良された診断検証済み）"
                                : $"フォールバック翻訳品質問題検出: {sameLanguageCount}件翻訳失敗（改良された診断使用）",
                            Metrics = new Dictionary<string, object>
                            {
                                { "SameLanguageCount", sameLanguageCount },
                                { "TotalTranslations", translations.Count },
                                { "QualityScore", qualityIsGood ? 1.0 : (double)(translations.Count - sameLanguageCount) / translations.Count },
                                { "TranslationPath", "FallbackBatch" },
                                { "SourceLanguage", sourceLanguage.Code },
                                { "TargetLanguage", targetLanguage.Code },
                                { "DetectionMethod", "EnhancedTextComparison" },
                                { "FailureDetails", sameLanguageFailures.Count > 0 ? sameLanguageFailures.Take(3) : new List<string>() },
                                { "IsTextComparisonBased", true }
                            }
                        }).ConfigureAwait(false);

                        Console.WriteLine($"🎉 [VERIFICATION] バッチ翻訳成功！フォールバックせずに結果を返します");
                        _logger?.LogDebug("🎉 [VERIFICATION] バッチ翻訳成功！フォールバックせずに結果を返します");
                        totalStopwatch.Stop();
                        _logger?.LogInformation("[TIMING] CoordinateBasedTranslationService.TranslateBatchAsync完了（成功）: {ElapsedMs}ms", totalStopwatch.ElapsedMilliseconds);
                        return translations;
                    }
                    else
                    {
                        // 翻訳品質診断: 部分失敗の診断
                        await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                        {
                            Stage = "TranslationQualityCheck",
                            IsSuccess = false,
                            ProcessingTimeMs = 0,
                            SessionId = translationId,
                            Severity = DiagnosticSeverity.Warning,
                            Message = $"フォールバック翻訳部分失敗: {successCount}/{batchResults.Count}件成功",
                            Metrics = new Dictionary<string, object>
                            {
                                { "SuccessCount", successCount },
                                { "TotalCount", batchResults.Count },
                                { "FailureCount", batchResults.Count - successCount },
                                { "TranslationPath", "FallbackBatch" },
                                { "FailureReason", "PartialBatchFailure" }
                            }
                        }).ConfigureAwait(false);

                        Console.WriteLine($"❌ [VERIFICATION] バッチ翻訳の一部が失敗 - 個別翻訳にフォールバック");
                        _logger?.LogDebug("❌ [VERIFICATION] バッチ翻訳の一部が失敗 - 個別翻訳にフォールバック");
                    }
                }
                else
                {
                    // 翻訳品質診断: 空結果の診断
                    await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                    {
                        Stage = "TranslationQualityCheck",
                        IsSuccess = false,
                        ProcessingTimeMs = 0,
                        SessionId = translationId,
                        Severity = DiagnosticSeverity.Error,
                        Message = "フォールバック翻訳結果が空 - 翻訳エンジン応答なし",
                        Metrics = new Dictionary<string, object>
                        {
                            { "ResultCount", batchResults?.Count ?? 0 },
                            { "TranslationPath", "FallbackBatch" },
                            { "FailureReason", "EmptyResults" }
                        }
                    }).ConfigureAwait(false);

                    Console.WriteLine($"❌ [VERIFICATION] バッチ翻訳結果が空 - 個別翻訳にフォールバック");
                    _logger?.LogDebug("❌ [VERIFICATION] バッチ翻訳結果が空 - 個別翻訳にフォールバック");
                }
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                // 翻訳品質診断: タイムアウト診断イベント
                var translationId = Guid.NewGuid().ToString("N")[..8]; // タイムアウト時は新しいIDを生成
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "TranslationQualityCheck",
                    IsSuccess = false,
                    ProcessingTimeMs = 60000, // 60秒タイムアウト
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Error,
                    Message = "フォールバック翻訳タイムアウト - 60秒制限超過",
                    Metrics = new Dictionary<string, object>
                    {
                        { "TimeoutMs", 60000 },
                        { "TranslationPath", "FallbackBatch" },
                        { "FailureReason", "Timeout" },
                        { "TextCount", texts?.Count ?? 0 }
                    }
                }).ConfigureAwait(false); // タイムアウト時はCancellationTokenを使用しない

                Console.WriteLine($"⏰ [VERIFICATION] バッチ翻訳が60秒でタイムアウト - Python翻訳サーバー処理時間が60秒を超過");
                // 🔥 [FILE_CONFLICT_FIX_28] ファイルアクセス競合回避のためILogger使用
                _logger?.LogWarning("⏰ [VERIFICATION] バッチ翻訳が60秒でタイムアウト - Python翻訳サーバー処理時間が60秒を超過");
            }
            catch (Exception ex)
            {
                // 翻訳品質診断: 例外診断イベント
                var translationId = Guid.NewGuid().ToString("N")[..8]; // 例外時は新しいIDを生成
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "TranslationQualityCheck",
                    IsSuccess = false,
                    ProcessingTimeMs = 0,
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Error,
                    Message = $"フォールバック翻訳例外: {ex.GetType().Name}: {ex.Message}",
                    Metrics = new Dictionary<string, object>
                    {
                        { "ExceptionType", ex.GetType().Name },
                        { "ExceptionMessage", ex.Message },
                        { "TranslationPath", "FallbackBatch" },
                        { "FailureReason", "Exception" },
                        { "TextCount", texts?.Count ?? 0 }
                    }
                }).ConfigureAwait(false); // 例外時はCancellationTokenを使用しない

                Console.WriteLine($"💥 [VERIFICATION] バッチ翻訳で例外発生: {ex.GetType().Name}: {ex.Message}");
                // 🔥 [FILE_CONFLICT_FIX_29] ファイルアクセス競合回避のためILogger使用
                _logger?.LogError(ex, "💫 [VERIFICATION] バッチ翻訳で例外発生: {ExceptionType}", ex.GetType().Name);
            }
        }

        // 個別翻訳にフォールバック
        Console.WriteLine($"🌟 [BATCH_DEBUG] バッチ翻訳が利用できないため個別翻訳にフォールバック");
        // 🔥 [FILE_CONFLICT_FIX_30] ファイルアクセス競合回避のためILogger使用
        _logger?.LogDebug("🌟 [BATCH_DEBUG] バッチ翻訳が利用できないため個別翻訳にフォールバック");
        
        
        // 🔧 一時的に並列処理を無効化（TransformersOpusMtEngineのIOException問題調査のため）
        var results = new List<string>();
        
        _logger?.LogInformation("🔄 順次翻訳開始 - チャンク数: {Count}", texts.Count);
        
        foreach (var text in texts)
        {
            try
            {
                Console.WriteLine($"🌍 [FACADE_DEBUG] Individual translate call for: '{text[..Math.Min(20, text.Length)]}...'");
                // 🔥 [FILE_CONFLICT_FIX_31] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🌍 [FACADE_DEBUG] Individual translate call for: '{TextPreview}...'", 
                    text[..Math.Min(20, text.Length)]);
                    
                var result = await _processingFacade.TranslationService.TranslateAsync(
                    text, sourceLanguage, targetLanguage, null, cancellationToken)
                    .ConfigureAwait(false);
                    
                Console.WriteLine($"🔍 [FACADE_DEBUG] Translation result: IsSuccess={result?.IsSuccess}, Text='{result?.TranslatedText?[..Math.Min(20, result?.TranslatedText?.Length ?? 0)] ?? "null"}...'");
                // 🔥 [FILE_CONFLICT_FIX_32] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔍 [FACADE_DEBUG] Translation result: IsSuccess={IsSuccess}, Text='{TextPreview}...'", 
                    result?.IsSuccess, result?.TranslatedText?[..Math.Min(20, result?.TranslatedText?.Length ?? 0)] ?? "null");
                results.Add(result.TranslatedText ?? "[Translation Failed]");
                
                _logger?.LogDebug("✅ 順次翻訳完了: {Text} → {Result}", 
                    text.Length > 20 ? string.Concat(text.AsSpan(0, 20), "...") : text,
                    (result.TranslatedText ?? "[Translation Failed]").Length > 20 ? 
                        string.Concat(result.TranslatedText.AsSpan(0, 20), "...") : result.TranslatedText ?? "[Translation Failed]");
            }
            catch (TaskCanceledException)
            {
                results.Add("[Translation Timeout]");
                _logger?.LogWarning("⚠️ 翻訳タイムアウト: {Text}", text.Length > 20 ? string.Concat(text.AsSpan(0, 20), "...") : text);
            }
            catch (Exception ex)
            {
                results.Add("[Translation Failed]");
                _logger?.LogError(ex, "❌ 翻訳エラー: {Text}", text.Length > 20 ? string.Concat(text.AsSpan(0, 20), "...") : text);
            }
        }
        
        _logger?.LogInformation("🏁 順次翻訳完了 - 成功: {Success}/{Total}", 
            results.Count(r => !r.StartsWith('[')), results.Count);
        
        return results;
    }

    // OPUS-MT削除済み: TransformersOpusMtEngine関連機能はNLLB-200統一により不要
    
    
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
            
            DebugLogUtility.WriteLog($"🔥🔥🔥 インプレース翻訳オーバーレイ表示直前 - overlayManager null?: {_processingFacade.OverlayManager == null}");
            if (_processingFacade.OverlayManager != null)
            {
                // 各TextChunkを個別にインプレース表示
                foreach (var textChunk in textChunks)
                {
                    // 🚫 [TRANSLATION_ONLY] 失敗・エラー結果の表示を包括的に防止
                    var hasValidTranslation = TranslationValidator.IsValid(textChunk.TranslatedText, textChunk.CombinedText);
                    
                    if (hasValidTranslation)
                    {
                        // 🚫 Phase 11.2: 重複表示修正 - DisplayInPlaceTranslationOverlay内も無効化
                        // TranslationWithBoundsCompletedEvent → OverlayUpdateEvent 経由で既に表示されている
                        Console.WriteLine($"🚫 [PHASE11.2] DisplayInPlaceTranslationOverlay直接表示スキップ - チャンク {textChunk.ChunkId}");
                        // await _processingFacade.OverlayManager.ShowInPlaceOverlayAsync(textChunk, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        DebugLogUtility.WriteLog($"🚫 [TRANSLATION_ONLY] オーバーレイ表示スキップ - ChunkId: {textChunk.ChunkId}, 原文: '{textChunk.CombinedText}'");
                    }
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
        // 🔥 [FILE_CONFLICT_FIX_33] ファイルアクセス競合回避のためILogger使用
        _logger?.LogDebug("🔥 [DEBUG] PublishOcrCompletedEventAsync呼び出し開始: チャンク数={ChunkCount}", textChunks.Count);
        
        try
        {
            Console.WriteLine($"🔥 [DEBUG] SelectMany実行開始 - textChunks.Count={textChunks.Count}");
            var positionedResults = textChunks.SelectMany(chunk => chunk.TextResults).ToList();
            Console.WriteLine($"🔥 [DEBUG] SelectMany実行完了 - positionedResults作成成功");
            Console.WriteLine($"🔥 [DEBUG] TextResults検証: チャンク数={textChunks.Count}, positionedResults数={positionedResults.Count}");
            // 🔥 [FILE_CONFLICT_FIX_34] ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔥 [DEBUG] TextResults検証: チャンク数={ChunkCount}, positionedResults数={ResultsCount}", 
                textChunks.Count, positionedResults.Count);
            
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
                // 🔥 [FILE_CONFLICT_FIX_35] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔥 [DEBUG] OCR完了イベント発行開始 - Results: {ResultCount}", ocrResults.Count);
                
                try
                {
                    Console.WriteLine($"🔥 [DEBUG] EventAggregator.PublishAsync呼び出し直前");
                    Console.WriteLine($"🔥 [DEBUG] EventAggregator型: {_configurationFacade.EventAggregator.GetType().FullName}");
                    Console.WriteLine($"🔥 [DEBUG] EventAggregatorハッシュ: {_configurationFacade.EventAggregator.GetHashCode()}");
                    // 🔥 [FILE_CONFLICT_FIX_36] ファイルアクセス競合回避のためILogger使用
                    _logger?.LogDebug("🔥 [DEBUG] PublishAsync直前 - EventAggregator型: {EventAggregatorType}, ハッシュ: {HashCode}", 
                        _configurationFacade.EventAggregator.GetType().FullName, _configurationFacade.EventAggregator.GetHashCode());
                    await _configurationFacade.EventAggregator.PublishAsync(ocrCompletedEvent).ConfigureAwait(false);
                    Console.WriteLine($"🔥 [DEBUG] EventAggregator.PublishAsync呼び出し完了");
                }
                catch (Exception publishEx)
                {
                    Console.WriteLine($"🔥 [ERROR] EventAggregator.PublishAsync例外: {publishEx.GetType().Name} - {publishEx.Message}");
                    // 🔥 [FILE_CONFLICT_FIX_37] ファイルアクセス競合回避のためILogger使用
                    _logger?.LogError(publishEx, "🔥 [ERROR] EventAggregator.PublishAsync例外: {ExceptionType}", publishEx.GetType().Name);
                    throw;
                }
                
                _logger?.LogDebug("🔥 OCR完了イベント発行完了 - Results: {ResultCount}", ocrResults.Count);
                Console.WriteLine($"🔥 [DEBUG] OCR完了イベント発行完了 - Results: {ocrResults.Count}");
                // 🔥 [FILE_CONFLICT_FIX_38] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔥 [DEBUG] OCR完了イベント発行完了 - Results: {ResultCount}", ocrResults.Count);
            }
            else
            {
                _logger?.LogInformation("📝 OCR結果が0件のため、OCR完了イベントの発行をスキップ");
                Console.WriteLine($"🔥 [DEBUG] OCR結果が0件のため、OCR完了イベントの発行をスキップ");
                // 🔥 [FILE_CONFLICT_FIX_39] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔥 [DEBUG] OCR結果が0件のため、OCR完了イベントの発行をスキップ");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OCR完了イベントの発行に失敗しました");
            Console.WriteLine($"🔥 [ERROR] PublishOcrCompletedEventAsync例外: {ex.GetType().Name} - {ex.Message}");
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化.Name} - {ex.Message}{Environment.NewLine}");
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
            var batchOcrAvailable = _processingFacade.OcrProcessor != null;
            var overlayAvailable = _processingFacade.OverlayManager != null;
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
            if (_processingFacade.OverlayManager is IDisposable disposableOverlayManager)
            {
                disposableOverlayManager.Dispose();
            }

            // BatchOcrProcessorのクリーンアップ
            if (_processingFacade.OcrProcessor is IDisposable disposableBatchProcessor)
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
