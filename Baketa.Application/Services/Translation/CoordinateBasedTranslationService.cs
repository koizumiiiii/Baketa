using System;
using System.Drawing;
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
    private readonly ITranslationProcessingFacade _processingFacade;
    private readonly IConfigurationFacade _configurationFacade;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CoordinateBasedTranslationService>? _logger;
    private bool _disposed;

    public CoordinateBasedTranslationService(
        ITranslationProcessingFacade processingFacade,
        IConfigurationFacade configurationFacade,
        IServiceProvider serviceProvider,
        ILogger<CoordinateBasedTranslationService>? logger = null)
    {
        _processingFacade = processingFacade ?? throw new ArgumentNullException(nameof(processingFacade));
        _configurationFacade = configurationFacade ?? throw new ArgumentNullException(nameof(configurationFacade));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger;
        
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
                AutoDetectSourceLanguage = translationSettings.AutoDetectSourceLanguage,
                DefaultSourceLanguage = translationSettings.DefaultSourceLanguage,
                DefaultTargetLanguage = translationSettings.DefaultTargetLanguage
            });
        }
        catch (Exception ex)
        {
            _configurationFacade.Logger?.LogError("CoordinateBasedTranslationService", "設定値の取得に失敗", ex);
        }
        
        _logger?.LogInformation("🚀 CoordinateBasedTranslationService initialized - Hash: {Hash}", this.GetHashCode());
    }

    /// <summary>
    /// 設定から言語ペアを取得（ユーザー設定を優先）
    /// </summary>
    private (Language sourceLanguage, Language targetLanguage) GetLanguagesFromSettings()
    {
        try
        {
            // 統一設定サービスから翻訳設定を取得
            var translationSettings = _configurationFacade.SettingsService.GetTranslationSettings();
            
            var sourceLanguageCode = translationSettings.AutoDetectSourceLanguage 
                ? "auto" 
                : translationSettings.DefaultSourceLanguage;
            var targetLanguageCode = translationSettings.DefaultTargetLanguage;
            
            Console.WriteLine($"🎯 [UNIFIED_SETTINGS] AutoDetect={translationSettings.AutoDetectSourceLanguage}, Source='{sourceLanguageCode}', Target='{targetLanguageCode}'");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 [UNIFIED_SETTINGS] AutoDetect={translationSettings.AutoDetectSourceLanguage}, Source='{sourceLanguageCode}', Target='{targetLanguageCode}'{Environment.NewLine}");

            // Language enumに変換（統一ユーティリティ使用）
            var sourceLanguage = LanguageCodeConverter.ToLanguageEnum(sourceLanguageCode, Language.English);
            var targetLanguage = LanguageCodeConverter.ToLanguageEnum(targetLanguageCode, Language.Japanese);

            Console.WriteLine($"🌍 [COORDINATE_SETTINGS] 最終言語設定: {sourceLanguageCode} → {targetLanguageCode}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🌍 [COORDINATE_SETTINGS] 最終言語設定: {sourceLanguageCode} → {targetLanguageCode}{Environment.NewLine}");

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
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 [DEBUG] ProcessWithCoordinateBasedTranslationAsync開始 - 画像: {image.Width}x{image.Height}{Environment.NewLine}");

            // バッチOCR処理でテキストチャンクを取得（詳細時間測定）
            var ocrMeasurement = new PerformanceMeasurement(
                MeasurementType.BatchOcrProcessing, 
                $"バッチOCR処理 - 画像:{image.Width}x{image.Height}")
                .WithAdditionalInfo($"WindowHandle:0x{windowHandle.ToInt64():X}");
            
            // 🚨 [CRITICAL_FIX] OCR処理直前ログ
            Console.WriteLine($"🚨 [CRITICAL_FIX] バッチOCR処理開始直前 - CancellationToken.IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_FIX] バッチOCR処理開始直前 - CancellationToken.IsCancellationRequested: {cancellationToken.IsCancellationRequested}{Environment.NewLine}");
            
            var textChunks = await _processingFacade.OcrProcessor.ProcessBatchAsync(image, windowHandle, cancellationToken)
                .ConfigureAwait(false);
            
            // 🚨 [CRITICAL_FIX] OCR処理完了直後ログ
            Console.WriteLine($"🚨 [CRITICAL_FIX] バッチOCR処理完了直後 - ChunkCount: {textChunks.Count}, CancellationToken.IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_FIX] バッチOCR処理完了直後 - ChunkCount: {textChunks.Count}, CancellationToken.IsCancellationRequested: {cancellationToken.IsCancellationRequested}{Environment.NewLine}");
            
            var ocrResult = ocrMeasurement.Complete();
            var ocrProcessingTime = ocrResult.Duration;
            
            _logger?.LogInformation("✅ バッチOCR完了 - チャンク数: {ChunkCount}, 処理時間: {ProcessingTime}ms", 
                textChunks.Count, ocrProcessingTime.TotalMilliseconds);
            
            // 🚨 [ROOT_CAUSE_FIX] OCR完了イベント発行を無効化してバッチ翻訳を直接実行
            Console.WriteLine($"🚨 [ROOT_CAUSE_FIX] OCR完了イベント発行をスキップ - 個別翻訳の大量発行を防止");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [ROOT_CAUSE_FIX] OCR完了イベント発行をスキップ - 個別翻訳の大量発行を防止{Environment.NewLine}");
                
            // await PublishOcrCompletedEventAsync(image, textChunks, ocrProcessingTime).ConfigureAwait(false);
            
            Console.WriteLine($"🚨 [ROOT_CAUSE_FIX] OCR完了イベント発行スキップ完了 - バッチ翻訳処理に移行");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [ROOT_CAUSE_FIX] OCR完了イベント発行スキップ完了 - バッチ翻訳処理に移行{Environment.NewLine}");
            
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
            Console.WriteLine($"🚨 [CRITICAL_FIX] バッチ翻訳処理開始直前 - チャンク数: {textChunks.Count}, CancellationToken.IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_FIX] バッチ翻訳処理開始直前 - チャンク数: {textChunks.Count}, CancellationToken.IsCancellationRequested: {cancellationToken.IsCancellationRequested}{Environment.NewLine}");
            
            _logger?.LogInformation("🌐 バッチ翻訳処理開始 - チャンク数: {Count}", textChunks.Count);
            DebugLogUtility.WriteLog($"🌐 バッチ翻訳処理開始 - チャンク数: {textChunks.Count}");
            
            // 翻訳サービスの詳細情報をログ出力
            var serviceType = _processingFacade.TranslationService.GetType().Name;
            DebugLogUtility.WriteLog($"🔧 使用中の翻訳サービス: {serviceType}");
            
            // 🚀 Phase 2: バッチ翻訳の実装
            Console.WriteLine($"🔍 [CHUNK_DEBUG] Total textChunks received: {textChunks.Count}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [CHUNK_DEBUG] Total textChunks received: {textChunks.Count}{Environment.NewLine}");
            
            // 空でないテキストチャンクを抽出
            var nonEmptyChunks = textChunks.Where(c => !string.IsNullOrWhiteSpace(c.CombinedText)).ToList();
            var emptyChunks = textChunks.Where(c => string.IsNullOrWhiteSpace(c.CombinedText)).ToList();
            
            Console.WriteLine($"🔍 [CHUNK_DEBUG] NonEmpty chunks: {nonEmptyChunks.Count}, Empty chunks: {emptyChunks.Count}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [CHUNK_DEBUG] NonEmpty chunks: {nonEmptyChunks.Count}, Empty chunks: {emptyChunks.Count}{Environment.NewLine}");
                
            // チャンク詳細をダンプ
            for (int i = 0; i < Math.Min(textChunks.Count, 3); i++)
            {
                var chunk = textChunks[i];
                Console.WriteLine($"🔍 [CHUNK_DEBUG] Chunk[{i}]: Text='{chunk.CombinedText}', IsEmpty={string.IsNullOrWhiteSpace(chunk.CombinedText)}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [CHUNK_DEBUG] Chunk[{i}]: Text='{chunk.CombinedText}', IsEmpty={string.IsNullOrWhiteSpace(chunk.CombinedText)}{Environment.NewLine}");
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
                    
                    // バッチ翻訳を試行（設定から言語を取得）
                    var (sourceLanguage, targetLanguage) = GetLanguagesFromSettings();
                    var batchResults = await TranslateBatchAsync(
                        batchTexts,
                        sourceLanguage,
                        targetLanguage,
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
                                
                            var (sourceLanguage, targetLanguage) = GetLanguagesFromSettings();
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
            else
            {
                Console.WriteLine($"❌ [CHUNK_DEBUG] No non-empty chunks found! Skipping translation.");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [CHUNK_DEBUG] No non-empty chunks found! Skipping translation.{Environment.NewLine}");
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
        catch (TaskCanceledException ex)
        {
            // 🚨 [CRITICAL_FIX] TaskCanceledException詳細をERRORレベルでログ出力
            _logger?.LogError(ex, "🚨 座標ベース翻訳処理がキャンセル/タイムアウトしました - これがバッチ翻訳実行されない根本原因");
            
            Console.WriteLine($"🚨 [CRITICAL_FIX] TaskCanceledException発生: {ex.Message}");
            Console.WriteLine($"🚨 [CRITICAL_FIX] CancellationToken.IsCancellationRequested: {ex.CancellationToken.IsCancellationRequested}");
            Console.WriteLine($"🚨 [CRITICAL_FIX] スタックトレース: {ex.StackTrace}");
            
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_FIX] TaskCanceledException発生: {ex.Message}{Environment.NewLine}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_FIX] CancellationToken.IsCancellationRequested: {ex.CancellationToken.IsCancellationRequested}{Environment.NewLine}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_FIX] スタックトレース: {ex.StackTrace?.Replace(Environment.NewLine, " | ")}{Environment.NewLine}");
            
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
        Console.WriteLine($"🚀 [FACADE_DEBUG] TranslationService via Facade: {_processingFacade.TranslationService?.GetType().Name}");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [FACADE_DEBUG] TranslationService via Facade: {_processingFacade.TranslationService?.GetType().Name}{Environment.NewLine}");
        
        // 🔍 [VERIFICATION] バッチ翻訳の実際の動作を検証
        var transformersEngine = _serviceProvider.GetService<TransformersOpusMtEngine>();
        if (transformersEngine != null)
        {
            Console.WriteLine($"🚀 [VERIFICATION] TransformersOpusMtEngine取得成功 - バッチ翻訳検証開始");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [VERIFICATION] TransformersOpusMtEngine取得成功 - バッチ翻訳検証開始{Environment.NewLine}");
                
            // Step 1: リクエストサイズの実測
            var direction = $"{sourceLanguage.Code}-{targetLanguage.Code}";
            var request = new { batch_texts = texts, direction = direction };
            var requestJson = System.Text.Json.JsonSerializer.Serialize(request) + "\n";
            var requestBytes = System.Text.Encoding.UTF8.GetBytes(requestJson);
            
            Console.WriteLine($"📏 [VERIFICATION] 実際のバッチリクエストサイズ: {requestBytes.Length} bytes");
            Console.WriteLine($"📄 [VERIFICATION] リクエストJSON preview: {requestJson.Substring(0, Math.Min(200, requestJson.Length))}...");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📏 [VERIFICATION] 実際のバッチリクエストサイズ: {requestBytes.Length} bytes{Environment.NewLine}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📄 [VERIFICATION] リクエストJSON preview: {requestJson.Substring(0, Math.Min(200, requestJson.Length))}...{Environment.NewLine}");
            
            // Step 2: タイムアウト付きバッチ翻訳実行
            try
            {
                var method = transformersEngine.GetType().GetMethod("TranslateBatchWithPersistentServerAsync", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (method != null)
                {
                    Console.WriteLine($"🎯 [VERIFICATION] バッチ翻訳メソッド発見 - 10秒タイムアウトで実行開始");
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 [VERIFICATION] バッチ翻訳メソッド発見 - 10秒タイムアウトで実行開始{Environment.NewLine}");
                    
                    // 10秒タイムアウトを設定
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    
                    var startTime = DateTime.Now;
                    var taskResult = method.Invoke(transformersEngine, new object[] { texts, direction, combinedCts.Token });
                    
                    if (taskResult is Task task)
                    {
                        Console.WriteLine($"⏱️ [VERIFICATION] Task実行中 - 開始時刻: {startTime:HH:mm:ss.fff}");
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⏱️ [VERIFICATION] Task実行中 - 開始時刻: {startTime:HH:mm:ss.fff}{Environment.NewLine}");
                        
                        await task.ConfigureAwait(false);
                        
                        var endTime = DateTime.Now;
                        var duration = endTime - startTime;
                        var batchResult = task.GetType().GetProperty("Result")?.GetValue(task);
                        
                        Console.WriteLine($"✅ [VERIFICATION] バッチ翻訳完了 - 実行時間: {duration.TotalMilliseconds:F0}ms");
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [VERIFICATION] バッチ翻訳完了 - 実行時間: {duration.TotalMilliseconds:F0}ms{Environment.NewLine}");
                        
                        // 結果を詳細分析
                        if (batchResult != null)
                        {
                            var successProperty = batchResult.GetType().GetProperty("Success");
                            var translationsProperty = batchResult.GetType().GetProperty("Translations");
                            var errorProperty = batchResult.GetType().GetProperty("Error");
                            
                            var isSuccess = successProperty?.GetValue(batchResult) as bool? ?? false;
                            var translations = translationsProperty?.GetValue(batchResult) as IList<string>;
                            var error = errorProperty?.GetValue(batchResult)?.ToString();
                            
                            Console.WriteLine($"🔍 [VERIFICATION] 結果分析: Success={isSuccess}, TranslationCount={translations?.Count ?? 0}, Error={error ?? "None"}");
                            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [VERIFICATION] 結果分析: Success={isSuccess}, TranslationCount={translations?.Count ?? 0}, Error={error ?? "None"}{Environment.NewLine}");
                            
                            if (isSuccess && translations != null)
                            {
                                Console.WriteLine($"🎉 [VERIFICATION] バッチ翻訳成功！フォールバックせずに結果を返します");
                                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎉 [VERIFICATION] バッチ翻訳成功！フォールバックせずに結果を返します{Environment.NewLine}");
                                return translations.ToList();
                            }
                            else
                            {
                                Console.WriteLine($"❌ [VERIFICATION] バッチ翻訳結果が失敗 - 個別翻訳にフォールバック");
                                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [VERIFICATION] バッチ翻訳結果が失敗 - 個別翻訳にフォールバック{Environment.NewLine}");
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"⏰ [VERIFICATION] バッチ翻訳が10秒でタイムアウト - これがハング問題の証拠");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⏰ [VERIFICATION] バッチ翻訳が10秒でタイムアウト - これがハング問題の証拠{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [VERIFICATION] バッチ翻訳で例外発生: {ex.GetType().Name}: {ex.Message}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [VERIFICATION] バッチ翻訳で例外発生: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
            }
        }

        // 個別翻訳にフォールバック
        Console.WriteLine($"🌟 [BATCH_DEBUG] バッチ翻訳が利用できないため個別翻訳にフォールバック");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🌟 [BATCH_DEBUG] バッチ翻訳が利用できないため個別翻訳にフォールバック{Environment.NewLine}");
        
        
        // 🔧 一時的に並列処理を無効化（TransformersOpusMtEngineのIOException問題調査のため）
        var results = new List<string>();
        
        _logger?.LogInformation("🔄 順次翻訳開始 - チャンク数: {Count}", texts.Count);
        
        foreach (var text in texts)
        {
            try
            {
                Console.WriteLine($"🌍 [FACADE_DEBUG] Individual translate call for: '{text.Substring(0, Math.Min(20, text.Length))}...'");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🌍 [FACADE_DEBUG] Individual translate call for: '{text.Substring(0, Math.Min(20, text.Length))}...'{Environment.NewLine}");
                    
                var result = await _processingFacade.TranslationService.TranslateAsync(
                    text, sourceLanguage, targetLanguage, null, cancellationToken)
                    .ConfigureAwait(false);
                    
                Console.WriteLine($"🔍 [FACADE_DEBUG] Translation result: IsSuccess={result?.IsSuccess}, Text='{result?.TranslatedText?.Substring(0, Math.Min(20, result?.TranslatedText?.Length ?? 0)) ?? "null"}...'");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [FACADE_DEBUG] Translation result: IsSuccess={result?.IsSuccess}, Text='{result?.TranslatedText?.Substring(0, Math.Min(20, result?.TranslatedText?.Length ?? 0)) ?? "null"}...'{Environment.NewLine}");
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
            _logger?.LogInformation("🔍 [BATCH_DEBUG] TryGetTransformersOpusMtEngine開始 - _translationService型: {ServiceType}", _processingFacade.TranslationService.GetType().Name);
            
            // 直接TransformersOpusMtEngineのインスタンスかチェック
            if (_processingFacade.TranslationService is TransformersOpusMtEngine directEngine)
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
            var serviceType = _processingFacade.TranslationService.GetType();
            _logger?.LogInformation("🔍 [BATCH_DEBUG] リフレクション探索開始 - 対象型: {ServiceType}", serviceType.Name);
            
            // DefaultTranslationServiceの_availableEnginesフィールドを確認
            var availableEnginesField = serviceType.GetField("_availableEngines", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (availableEnginesField != null)
            {
                _logger?.LogInformation("🔍 [BATCH_DEBUG] _availableEnginesフィールド発見");
                if (availableEnginesField.GetValue(_processingFacade.TranslationService) is IEnumerable<object> availableEngines)
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
            if (enginesField?.GetValue(_processingFacade.TranslationService) is IEnumerable<object> engines)
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
            
            DebugLogUtility.WriteLog($"🔥🔥🔥 インプレース翻訳オーバーレイ表示直前 - overlayManager null?: {_processingFacade.OverlayManager == null}");
            if (_processingFacade.OverlayManager != null)
            {
                // 各TextChunkを個別にインプレース表示
                foreach (var textChunk in textChunks)
                {
                    await _processingFacade.OverlayManager.ShowInPlaceOverlayAsync(textChunk, cancellationToken)
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
                    Console.WriteLine($"🔥 [DEBUG] EventAggregator型: {_configurationFacade.EventAggregator.GetType().FullName}");
                    Console.WriteLine($"🔥 [DEBUG] EventAggregatorハッシュ: {_configurationFacade.EventAggregator.GetHashCode()}");
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] PublishAsync直前 - EventAggregator型: {_configurationFacade.EventAggregator.GetType().FullName}, ハッシュ: {_configurationFacade.EventAggregator.GetHashCode()}{Environment.NewLine}");
                    await _configurationFacade.EventAggregator.PublishAsync(ocrCompletedEvent).ConfigureAwait(false);
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