using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services; // 🔥 [COORDINATE_FIX] ICoordinateTransformationService用
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Events.Translation;
using Baketa.Core.Events.EventTypes; // 🔥 [INDIVIDUAL_TRANSLATION_EVENT] TranslationWithBoundsCompletedEvent用
using Baketa.Core.Translation.Models;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Logging;
using Baketa.Core.Models.Translation;
using Language = Baketa.Core.Translation.Models.Language;

namespace Baketa.Application.EventHandlers.Translation;

/// <summary>
/// 集約済みチャンクに対してバッチ翻訳を実行するイベントハンドラ
/// Phase 12.2: 2重翻訳アーキテクチャ排除の中核実装
///
/// TimedChunkAggregatorから発行されるAggregatedChunksReadyEventを受信し、
/// CoordinateBasedTranslationService.ProcessBatchTranslationAsync()相当の処理を実行
/// </summary>
public sealed class AggregatedChunksReadyEventHandler : IEventProcessor<AggregatedChunksReadyEvent>
{
    // 🔥 [PHASE1_SEMAPHORE] 翻訳実行制御用セマフォ（1並列のみ許可）
    // Gemini推奨の多層防御アーキテクチャ - 第2層: 物理的排他制御
    private static readonly SemaphoreSlim _translationExecutionSemaphore = new(1, 1);

    private readonly Baketa.Core.Abstractions.Translation.ITranslationService _translationService;
    private readonly IStreamingTranslationService? _streamingTranslationService;
    private readonly IInPlaceTranslationOverlayManager _overlayManager;
    private readonly ILanguageConfigurationService _languageConfig;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<AggregatedChunksReadyEventHandler> _logger;
    private readonly ICoordinateTransformationService _coordinateTransformationService; // 🔥 [COORDINATE_FIX]

    public AggregatedChunksReadyEventHandler(
        Baketa.Core.Abstractions.Translation.ITranslationService translationService,
        IInPlaceTranslationOverlayManager overlayManager,
        ILanguageConfigurationService languageConfig,
        IEventAggregator eventAggregator,
        ILogger<AggregatedChunksReadyEventHandler> logger,
        ICoordinateTransformationService coordinateTransformationService, // 🔥 [COORDINATE_FIX]
        IStreamingTranslationService? streamingTranslationService = null)
    {
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _coordinateTransformationService = coordinateTransformationService ?? throw new ArgumentNullException(nameof(coordinateTransformationService)); // 🔥 [COORDINATE_FIX]
        _streamingTranslationService = streamingTranslationService;
    }

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool SynchronousExecution => false; // 🔧 [FIX] 並列処理を許可して120秒ブロック時のデッドロック回避

    /// <summary>
    /// 🔥 [STOP_CLEANUP] Stop時のセマフォ強制リセット
    /// 問題: タイムアウト中（0-10秒）にStopしても、セマフォが保持されたまま残る
    /// 解決策: Stop時にセマフォの状態を強制的にリセットし、次のStartで即座に翻訳可能にする
    /// </summary>
    public static void ResetSemaphoreForStop()
    {
        try
        {
            Console.WriteLine($"🔍 [STOP_CLEANUP_DEBUG] メソッド開始 - CurrentCount: {_translationExecutionSemaphore.CurrentCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 [STOP_CLEANUP_DEBUG] Console.WriteLine失敗: {ex.GetType().Name} - {ex.Message}");
        }

        // セマフォが既に取得されている場合（CurrentCount == 0）のみリセット
        if (_translationExecutionSemaphore.CurrentCount == 0)
        {
            try
            {
                _translationExecutionSemaphore.Release();
                Console.WriteLine("🔓 [STOP_CLEANUP] セマフォ強制解放完了 - Stop時クリーンアップ");
            }
            catch (SemaphoreFullException)
            {
                // 既に解放済み（CurrentCount == 1）の場合は無視
                Console.WriteLine("ℹ️ [STOP_CLEANUP] セマフォは既に解放済み");
            }
        }
        else
        {
            Console.WriteLine($"ℹ️ [STOP_CLEANUP] セマフォは既に利用可能 - CurrentCount: {_translationExecutionSemaphore.CurrentCount}");
        }
    }

    /// <inheritdoc />
    public async Task HandleAsync(AggregatedChunksReadyEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // 🔥 [PHASE1_SEMAPHORE] セマフォ取得（並行実行防止）
        // WaitAsync(0) = 即座に判定、ブロッキングなし
        if (!await _translationExecutionSemaphore.WaitAsync(0).ConfigureAwait(false))
        {
            // 既に翻訳実行中の場合はスキップ
            _logger.LogWarning("⚠️ [PHASE1] 翻訳実行中のため、SessionId: {SessionId} をスキップ（並行実行防止）",
                eventData.SessionId);

            // 🔥 [GEMINI_FEEDBACK] UI/UXフィードバック強化
            _logger?.LogDebug($"⏳ [PHASE1] 翻訳スキップ - 別の翻訳実行中（SessionId: {eventData.SessionId}）");
            Console.WriteLine($"⏳ [PHASE1] 翻訳スキップ - 別の翻訳実行中（SessionId: {eventData.SessionId}）");

            return; // 早期リターン - イベント破棄
        }

        // 🔥 [PHASE12.2_NEW_ARCH] Gemini推奨の見える化ログ
        Console.WriteLine($"✅✅✅ [PHASE12.2_NEW_ARCH] AggregatedChunksReadyEventHandler開始. SessionId: {eventData.SessionId}, ChunkCount: {eventData.AggregatedChunks.Count}");
        _logger?.LogDebug($"✅✅✅ [PHASE12.2_NEW_ARCH] AggregatedChunksReadyEventHandler開始. SessionId: {eventData.SessionId}, ChunkCount: {eventData.AggregatedChunks.Count}");

        try
        {
            // 🔥 確実なログ出力（ファイル直接書き込み）
            _logger?.LogDebug($"🔥🔥🔥 [PHASE12.2_HANDLER] HandleAsync tryブロック開始 - SessionId: {eventData.SessionId}, ChunkCount: {eventData.AggregatedChunks.Count}");
            Console.WriteLine($"🔥🔥🔥 [PHASE12.2_HANDLER] HandleAsync tryブロック開始 - SessionId: {eventData.SessionId}, ChunkCount: {eventData.AggregatedChunks.Count}");

            _logger.LogInformation("🔥 [PHASE12.2] 集約チャンク受信 - {Count}個, SessionId: {SessionId}",
                eventData.AggregatedChunks.Count, eventData.SessionId);
            _logger.LogCritical("✅✅✅ [PHASE12.2_NEW_ARCH] AggregatedChunksReadyEventHandler開始. SessionId: {SessionId}", eventData.SessionId);

            // 集約されたチャンクをリストに変換
            var aggregatedChunks = eventData.AggregatedChunks.ToList();

            // 空でないチャンクのみフィルタリング
            var nonEmptyChunks = aggregatedChunks
                .Where(chunk => !string.IsNullOrWhiteSpace(chunk.CombinedText))
                .ToList();

            // 空のチャンクに空文字列を設定
            foreach (var emptyChunk in aggregatedChunks.Where(c => string.IsNullOrWhiteSpace(c.CombinedText)))
            {
                emptyChunk.TranslatedText = "";
            }

            if (nonEmptyChunks.Count == 0)
            {
                _logger.LogWarning("⚠️ [PHASE12.2] 翻訳可能なチャンクが0個 - 処理スキップ");
                return;
            }

            // バッチ翻訳実行
            _logger?.LogDebug($"🚀🚀🚀 [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");
            Console.WriteLine($"🚀🚀🚀 [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");

            var translationResults = await ExecuteBatchTranslationAsync(
                nonEmptyChunks,
                CancellationToken.None).ConfigureAwait(false);

            _logger?.LogDebug($"✅✅✅ [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync完了 - 結果数: {translationResults.Count}");
            Console.WriteLine($"✅✅✅ [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync完了 - 結果数: {translationResults.Count}");

            // 翻訳結果を各チャンクに設定
            for (int i = 0; i < Math.Min(nonEmptyChunks.Count, translationResults.Count); i++)
            {
                nonEmptyChunks[i].TranslatedText = translationResults[i];
                _logger?.LogDebug($"🔧 [PHASE12.2_HANDLER] チャンク{i}翻訳結果設定: '{nonEmptyChunks[i].CombinedText}' → '{translationResults[i]}'");
            }

            // 🔥 [OVERLAY_FIX] 直接SimpleInPlaceOverlayManager.ShowInPlaceOverlayAsync()を呼び出し
            // Gemini推奨: TranslationWithBoundsCompletedEventを経由せず、直接オーバーレイ表示
            // 理由: イベントハンドラー未実装により表示されない問題を解決
            // アーキテクチャ: Application層 → Core層(IInPlaceTranslationOverlayManager)への依存は正しい（DIP準拠）
            _logger?.LogDebug($"🔥 [OVERLAY_FIX] 直接オーバーレイ表示開始 - チャンク数: {nonEmptyChunks.Count}");
            Console.WriteLine($"🔥 [OVERLAY_FIX] 直接オーバーレイ表示開始 - チャンク数: {nonEmptyChunks.Count}");

            for (int i = 0; i < Math.Min(nonEmptyChunks.Count, translationResults.Count); i++)
            {
                var chunk = nonEmptyChunks[i];
                // chunk.TranslatedTextは既にLine 176で設定済み

                // 🔥 [COORDINATE_FIX] ROI座標 → スクリーン絶対座標変換
                // 垂直モニター配置（セカンダリが上: Y=-1080~0, プライマリ: Y=0~1080）に対応
                var roiBounds = chunk.CombinedBounds;

                // 🔥 [PHASE2.1] ボーダーレス/フルスクリーン検出
                var isBorderlessOrFullscreen = _coordinateTransformationService.DetectBorderlessOrFullscreen(chunk.SourceWindowHandle);
                _logger.LogDebug("🔍 [PHASE2.1_DETECTION] ボーダーレス/フルスクリーン検出結果: {IsBorderless}, Handle: {Handle}",
                    isBorderlessOrFullscreen, chunk.SourceWindowHandle);

                var screenBounds = _coordinateTransformationService.ConvertRoiToScreenCoordinates(
                    roiBounds,
                    chunk.SourceWindowHandle,
                    roiScaleFactor: 1.0f,
                    isBorderlessOrFullscreen: isBorderlessOrFullscreen);

                _logger.LogDebug("🔥 [COORDINATE_FIX] ROI→Screen変換完了 - ROI:({RoiX},{RoiY},{RoiW}x{RoiH}), Screen:({ScreenX},{ScreenY},{ScreenW}x{ScreenH})",
                    roiBounds.X, roiBounds.Y, roiBounds.Width, roiBounds.Height,
                    screenBounds.X, screenBounds.Y, screenBounds.Width, screenBounds.Height);

                // 変換後の座標で新しいチャンクインスタンスを作成
                // AverageConfidenceは計算プロパティのため、TextResultsから自動計算される
                var chunkWithScreenCoords = new TextChunk
                {
                    ChunkId = chunk.ChunkId,
                    TextResults = chunk.TextResults,
                    CombinedBounds = screenBounds, // スクリーン絶対座標
                    CombinedText = chunk.CombinedText,
                    TranslatedText = chunk.TranslatedText,
                    SourceWindowHandle = chunk.SourceWindowHandle,
                    DetectedLanguage = chunk.DetectedLanguage
                };

                // 🔥 [OVERLAY_FIX] 直接オーバーレイ表示を呼び出し（スクリーン絶対座標使用）
                await _overlayManager.ShowInPlaceOverlayAsync(chunkWithScreenCoords, CancellationToken.None)
                    .ConfigureAwait(false);

                _logger?.LogDebug($"✅ [OVERLAY_FIX] チャンク{i}オーバーレイ表示完了 - Text: '{chunk.TranslatedText}', Bounds: ({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y},{chunk.CombinedBounds.Width}x{chunk.CombinedBounds.Height})");
                Console.WriteLine($"✅ [OVERLAY_FIX] チャンク{i}オーバーレイ表示完了 - Text: '{chunk.TranslatedText}'");
            }

            Console.WriteLine($"✅✅✅ [OVERLAY_FIX] オーバーレイ表示完了 - {nonEmptyChunks.Count}個表示");

            _logger.LogInformation("✅ [PHASE12.2] バッチ翻訳・個別イベント発行完了 - SessionId: {SessionId}, 翻訳数: {Count}",
                eventData.SessionId, translationResults.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE12.2] 集約チャンクイベント処理エラー - フォールバックイベント発行 - SessionId: {SessionId}",
                eventData.SessionId);

            // 🔥 [FALLBACK] 個別翻訳失敗時にフォールバックイベントを発行
            // AggregatedChunksFailedEventを発行し、CoordinateBasedTranslationServiceが全画面一括翻訳を実行
            try
            {
                var sourceLanguage = _languageConfig.GetSourceLanguageCode();
                var targetLanguage = _languageConfig.GetTargetLanguageCode();

                var failedEvent = new AggregatedChunksFailedEvent
                {
                    SessionId = eventData.SessionId,
                    FailedChunks = eventData.AggregatedChunks.ToList(),
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    ErrorMessage = ex.Message,
                    ErrorException = ex
                };

                await _eventAggregator.PublishAsync(failedEvent).ConfigureAwait(false);
                _logger.LogInformation("✅ [FALLBACK] AggregatedChunksFailedEvent発行完了 - SessionId: {SessionId}",
                    eventData.SessionId);
            }
            catch (Exception publishEx)
            {
                _logger.LogError(publishEx, "❌ [FALLBACK] AggregatedChunksFailedEvent発行失敗 - SessionId: {SessionId}",
                    eventData.SessionId);
            }

            // 例外を再スローせず正常終了（フォールバック処理に委ねる）
        }
        finally
        {
            // 🔥 [PHASE1_SEMAPHORE] セマフォ解放（必ず実行）
            _translationExecutionSemaphore.Release();
            _logger?.LogDebug($"🔓 [PHASE1] セマフォ解放完了 - SessionId: {eventData.SessionId}");
        }
    }

    /// <summary>
    /// バッチ翻訳実行
    /// CoordinateBasedTranslationService.ProcessBatchTranslationAsync()のLine 363-450相当の処理
    /// </summary>
    private async Task<List<string>> ExecuteBatchTranslationAsync(
        List<TextChunk> chunks,
        CancellationToken cancellationToken)
    {
        // 🔥 メソッド開始を確実に記録
        _logger?.LogDebug($"🎯🎯🎯 [PHASE12.2_BATCH] ExecuteBatchTranslationAsync メソッド開始 - ChunkCount: {chunks.Count}");
        Console.WriteLine($"🎯🎯🎯 [PHASE12.2_BATCH] ExecuteBatchTranslationAsync メソッド開始 - ChunkCount: {chunks.Count}");

        var batchTexts = chunks.Select(c => c.CombinedText).ToList();

        _logger?.LogDebug($"🎯 [PHASE12.2_BATCH] バッチテキスト作成完了 - テキスト数: {batchTexts.Count}");

        try
        {
            _logger?.LogDebug($"🚀 [PHASE12.2_BATCH] バッチ翻訳試行開始 - テキスト数: {batchTexts.Count}");
            _logger.LogInformation("🚀 [PHASE12.2] バッチ翻訳試行開始 - テキスト数: {Count}", batchTexts.Count);

            // ストリーミング翻訳サービスが利用可能な場合はそれを使用
            if (_streamingTranslationService != null)
            {
                _logger?.LogDebug($"🔥 [PHASE12.2_BATCH] ストリーミング翻訳サービス使用");
                _logger.LogDebug("🔥 [PHASE12.2] ストリーミング翻訳サービス使用");

                // CoordinateBasedTranslationServiceと同じシグネチャ
                _logger?.LogDebug($"📞 [PHASE12.2_BATCH] TranslateBatchWithStreamingAsync呼び出し直前");

                // 🔥 [PHASE3.1_FIX] 設定から言語ペア取得（ハードコード削除）
                var languagePair = _languageConfig.GetCurrentLanguagePair();
                var sourceLanguage = Language.FromCode(languagePair.SourceCode);
                var targetLanguage = Language.FromCode(languagePair.TargetCode);

                _logger?.LogDebug($"🌍 [PHASE3.1_FIX] 言語ペア取得完了 - {languagePair.SourceCode} → {languagePair.TargetCode}");
                Console.WriteLine($"🌍 [PHASE3.1_FIX] 言語ペア取得完了 - {languagePair.SourceCode} → {languagePair.TargetCode}");

                // 🚨🚨🚨 [ULTRA_CRITICAL] 呼び出し直前を確実に記録
                var timestamp1 = DateTime.Now.ToString("HH:mm:ss.fff");
                var threadId1 = Environment.CurrentManagedThreadId;
                System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                    $"[{timestamp1}][T{threadId1:D2}] 🚨🚨🚨 [ULTRA_CRITICAL] TranslateBatchWithStreamingAsync呼び出し実行！\r\n");

                var results = await _streamingTranslationService.TranslateBatchWithStreamingAsync(
                    batchTexts,
                    sourceLanguage,
                    targetLanguage,
                    null!, // OnChunkCompletedコールバックは不要（バッチ完了後にオーバーレイ表示）
                    cancellationToken).ConfigureAwait(false);

                // 🚨🚨🚨 [ULTRA_CRITICAL] 呼び出し完了を確実に記録
                var timestamp2 = DateTime.Now.ToString("HH:mm:ss.fff");
                var threadId2 = Environment.CurrentManagedThreadId;
                System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                    $"[{timestamp2}][T{threadId2:D2}] 🚨🚨🚨 [ULTRA_CRITICAL] TranslateBatchWithStreamingAsync呼び出し完了！ - 結果数: {results.Count}\r\n");

                _logger?.LogDebug($"✅ [PHASE12.2_BATCH] TranslateBatchWithStreamingAsync完了 - 結果数: {results.Count}");
                return results;
            }
            else
            {
                // 通常の翻訳サービスを使用
                _logger?.LogDebug($"🔥🔥🔥 [PHASE12.2_BATCH] DefaultTranslationService使用（_streamingTranslationService is null）");
                Console.WriteLine($"🔥🔥🔥 [PHASE12.2_BATCH] DefaultTranslationService使用（_streamingTranslationService is null）");
                _logger.LogDebug("🔥 [PHASE12.2] DefaultTranslationService使用");

                // 🔥 [PHASE3.1_FIX] 設定から言語ペア取得（ハードコード削除）
                var languagePair = _languageConfig.GetCurrentLanguagePair();
                var sourceLanguage = Language.FromCode(languagePair.SourceCode);
                var targetLanguage = Language.FromCode(languagePair.TargetCode);

                _logger?.LogDebug($"🌍 [PHASE3.1_FIX] 言語ペア取得完了 - {languagePair.SourceCode} → {languagePair.TargetCode}");
                Console.WriteLine($"🌍 [PHASE3.1_FIX] 言語ペア取得完了 - {languagePair.SourceCode} → {languagePair.TargetCode}");

                var results = new List<string>();
                for (int i = 0; i < batchTexts.Count; i++)
                {
                    var text = batchTexts[i];
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger?.LogDebug($"⚠️ [PHASE12.2_BATCH] キャンセル要求検出 - Index: {i}");
                        break;
                    }

                    _logger?.LogDebug($"📞📞📞 [PHASE12.2_BATCH] TranslateAsync呼び出し直前 - Index: {i}, Text: '{text}'");
                    Console.WriteLine($"📞📞📞 [PHASE12.2_BATCH] TranslateAsync呼び出し直前 - Index: {i}, Text: '{text}'");

                    var response = await _translationService.TranslateAsync(
                        text,
                        sourceLanguage,
                        targetLanguage,
                        null,
                        cancellationToken).ConfigureAwait(false);

                    _logger?.LogDebug($"✅✅✅ [PHASE12.2_BATCH] TranslateAsync完了 - Index: {i}, TranslatedText: '{response.TranslatedText}'");
                    Console.WriteLine($"✅✅✅ [PHASE12.2_BATCH] TranslateAsync完了 - Index: {i}, TranslatedText: '{response.TranslatedText}'");

                    results.Add(response.TranslatedText);
                }

                _logger?.LogDebug($"✅ [PHASE12.2_BATCH] DefaultTranslationService完了 - 結果数: {results.Count}");
                return results;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE12.2] バッチ翻訳処理エラー");
            throw;
        }
    }

    /// <summary>
    /// オーバーレイ表示処理
    /// CoordinateBasedTranslationService.ProcessBatchTranslationAsync()のオーバーレイ表示処理相当
    /// </summary>
    private async Task DisplayTranslationOverlayAsync(
        List<TextChunk> translatedChunks,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogDebug($"🎯🎯🎯 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync メソッド開始 - ChunkCount: {translatedChunks.Count}");
            Console.WriteLine($"🎯🎯🎯 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync メソッド開始 - ChunkCount: {translatedChunks.Count}");

            _logger.LogInformation("🎯 [PHASE12.2] インプレースオーバーレイ表示開始 - チャンク数: {Count}",
                translatedChunks.Count);

            // 翻訳結果の詳細ログ
            for (int i = 0; i < translatedChunks.Count; i++)
            {
                var chunk = translatedChunks[i];
                _logger?.LogDebug($"   🔍 [PHASE12.2_OVERLAY] チャンク[{i}]: '{chunk.CombinedText}' → '{chunk.TranslatedText}'");
                _logger.LogDebug("   [{Index}] '{Original}' → '{Translated}'",
                    i, chunk.CombinedText, chunk.TranslatedText);
            }

            // 各チャンクをインプレース表示
            int displayedCount = 0;
            foreach (var chunk in translatedChunks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogDebug($"⚠️ [PHASE12.2_OVERLAY] キャンセル要求検出 - 表示中断");
                    break;
                }

                if (chunk.CanShowInPlace() && !string.IsNullOrWhiteSpace(chunk.TranslatedText))
                {
                    _logger?.LogDebug($"🔥 [PHASE12.2_OVERLAY] ShowInPlaceOverlayAsync実行開始 - ChunkId: {chunk.ChunkId}");
                    _logger.LogDebug("🔥 [PHASE12.2] ShowInPlaceOverlayAsync実行 - ChunkId: {ChunkId}",
                        chunk.ChunkId);

                    await _overlayManager.ShowInPlaceOverlayAsync(chunk).ConfigureAwait(false);

                    displayedCount++;
                    _logger?.LogDebug($"   ✅ [PHASE12.2_OVERLAY] ShowInPlaceOverlayAsync完了 - ChunkId: {chunk.ChunkId}, 累計表示: {displayedCount}個");
                    _logger.LogDebug("   ✅ [PHASE12.2] インプレース表示完了 - ChunkId: {ChunkId}",
                        chunk.ChunkId);
                }
                else
                {
                    _logger?.LogDebug($"⚠️ [PHASE12.2_OVERLAY] スキップ - ChunkId: {chunk.ChunkId}, CanShowInPlace: {chunk.CanShowInPlace()}, HasTranslation: {!string.IsNullOrWhiteSpace(chunk.TranslatedText)}");
                }
            }

            _logger?.LogDebug($"🎉🎉🎉 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync完了 - 表示数: {displayedCount}/{translatedChunks.Count}");
            Console.WriteLine($"🎉🎉🎉 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync完了 - 表示数: {displayedCount}/{translatedChunks.Count}");

            _logger.LogInformation("🎉 [PHASE12.2] 座標ベース翻訳処理完了 - オーバーレイ表示成功");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"❌❌❌ [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync例外: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"❌❌❌ [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync例外: {ex.GetType().Name} - {ex.Message}");
            _logger.LogError(ex, "❌ [PHASE12.2] オーバーレイ表示エラー");
            throw;
        }
    }
}
