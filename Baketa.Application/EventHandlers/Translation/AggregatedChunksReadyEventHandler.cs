using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Events.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Logging;

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
    private readonly ITranslationService _translationService;
    private readonly IStreamingTranslationService? _streamingTranslationService;
    private readonly IInPlaceTranslationOverlayManager _overlayManager;
    private readonly ILogger<AggregatedChunksReadyEventHandler> _logger;

    public AggregatedChunksReadyEventHandler(
        ITranslationService translationService,
        IInPlaceTranslationOverlayManager overlayManager,
        ILogger<AggregatedChunksReadyEventHandler> logger,
        IStreamingTranslationService? streamingTranslationService = null)
    {
        Console.WriteLine("🚨🚨🚨 [CTOR_DEBUG] AggregatedChunksReadyEventHandler コンストラクター開始");
        DebugLogUtility.WriteLog("🚨🚨🚨 [CTOR_DEBUG] AggregatedChunksReadyEventHandler コンストラクター開始");

        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _streamingTranslationService = streamingTranslationService;

        var streamingServiceType = streamingTranslationService?.GetType().Name ?? "NULL";
        Console.WriteLine($"✅ [CTOR_DEBUG] AggregatedChunksReadyEventHandler初期化完了 - StreamingService型: {streamingServiceType}");
        DebugLogUtility.WriteLog($"✅ [CTOR_DEBUG] AggregatedChunksReadyEventHandler初期化完了 - StreamingService型: {streamingServiceType}");
    }

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool SynchronousExecution => true; // 🔥 [PHASE12.2_FIX] Task.Runのfire-and-forget問題を回避

    /// <inheritdoc />
    public async Task HandleAsync(AggregatedChunksReadyEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // 🔥 [PHASE12.2_NEW_ARCH] Gemini推奨の見える化ログ
        Console.WriteLine($"✅✅✅ [PHASE12.2_NEW_ARCH] AggregatedChunksReadyEventHandler開始. SessionId: {eventData.SessionId}, ChunkCount: {eventData.AggregatedChunks.Count}");
        DebugLogUtility.WriteLog($"✅✅✅ [PHASE12.2_NEW_ARCH] AggregatedChunksReadyEventHandler開始. SessionId: {eventData.SessionId}, ChunkCount: {eventData.AggregatedChunks.Count}");

        try
        {
            // 🔥 確実なログ出力（ファイル直接書き込み）
            DebugLogUtility.WriteLog($"🔥🔥🔥 [PHASE12.2_HANDLER] HandleAsync tryブロック開始 - SessionId: {eventData.SessionId}, ChunkCount: {eventData.AggregatedChunks.Count}");
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
            DebugLogUtility.WriteLog($"🚀🚀🚀 [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");
            Console.WriteLine($"🚀🚀🚀 [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");

            var translationResults = await ExecuteBatchTranslationAsync(
                nonEmptyChunks,
                CancellationToken.None).ConfigureAwait(false);

            DebugLogUtility.WriteLog($"✅✅✅ [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync完了 - 結果数: {translationResults.Count}");
            Console.WriteLine($"✅✅✅ [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync完了 - 結果数: {translationResults.Count}");

            // 翻訳結果を各チャンクに設定
            for (int i = 0; i < Math.Min(nonEmptyChunks.Count, translationResults.Count); i++)
            {
                nonEmptyChunks[i].TranslatedText = translationResults[i];
                DebugLogUtility.WriteLog($"🔧 [PHASE12.2_HANDLER] チャンク{i}翻訳結果設定: '{nonEmptyChunks[i].CombinedText}' → '{translationResults[i]}'");
            }

            // オーバーレイ表示
            DebugLogUtility.WriteLog($"🎯🎯🎯 [PHASE12.2_HANDLER] DisplayTranslationOverlayAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");
            Console.WriteLine($"🎯🎯🎯 [PHASE12.2_HANDLER] DisplayTranslationOverlayAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");

            await DisplayTranslationOverlayAsync(
                nonEmptyChunks,
                eventData.SourceWindowHandle,
                CancellationToken.None).ConfigureAwait(false);

            DebugLogUtility.WriteLog($"✅✅✅ [PHASE12.2_HANDLER] DisplayTranslationOverlayAsync完了 - SessionId: {eventData.SessionId}");
            Console.WriteLine($"✅✅✅ [PHASE12.2_HANDLER] DisplayTranslationOverlayAsync完了 - SessionId: {eventData.SessionId}");

            _logger.LogInformation("✅ [PHASE12.2] バッチ翻訳・オーバーレイ表示完了 - SessionId: {SessionId}, 翻訳数: {Count}",
                eventData.SessionId, translationResults.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE12.2] 集約チャンクイベント処理エラー - SessionId: {SessionId}",
                eventData.SessionId);
            throw;
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
        DebugLogUtility.WriteLog($"🎯🎯🎯 [PHASE12.2_BATCH] ExecuteBatchTranslationAsync メソッド開始 - ChunkCount: {chunks.Count}");
        Console.WriteLine($"🎯🎯🎯 [PHASE12.2_BATCH] ExecuteBatchTranslationAsync メソッド開始 - ChunkCount: {chunks.Count}");

        var batchTexts = chunks.Select(c => c.CombinedText).ToList();

        DebugLogUtility.WriteLog($"🎯 [PHASE12.2_BATCH] バッチテキスト作成完了 - テキスト数: {batchTexts.Count}");

        try
        {
            DebugLogUtility.WriteLog($"🚀 [PHASE12.2_BATCH] バッチ翻訳試行開始 - テキスト数: {batchTexts.Count}");
            _logger.LogInformation("🚀 [PHASE12.2] バッチ翻訳試行開始 - テキスト数: {Count}", batchTexts.Count);

            // ストリーミング翻訳サービスが利用可能な場合はそれを使用
            if (_streamingTranslationService != null)
            {
                DebugLogUtility.WriteLog($"🔥 [PHASE12.2_BATCH] ストリーミング翻訳サービス使用");
                _logger.LogDebug("🔥 [PHASE12.2] ストリーミング翻訳サービス使用");

                // CoordinateBasedTranslationServiceと同じシグネチャ
                DebugLogUtility.WriteLog($"📞 [PHASE12.2_BATCH] TranslateBatchWithStreamingAsync呼び出し直前");

                // 🚨🚨🚨 [ULTRA_CRITICAL] 呼び出し直前を確実に記録
                var timestamp1 = DateTime.Now.ToString("HH:mm:ss.fff");
                var threadId1 = Environment.CurrentManagedThreadId;
                System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                    $"[{timestamp1}][T{threadId1:D2}] 🚨🚨🚨 [ULTRA_CRITICAL] TranslateBatchWithStreamingAsync呼び出し実行！\r\n");

                var results = await _streamingTranslationService.TranslateBatchWithStreamingAsync(
                    batchTexts,
                    Language.FromCode("ja"), // TODO: 設定から取得
                    Language.FromCode("en"), // TODO: 設定から取得
                    null!, // OnChunkCompletedコールバックは不要（バッチ完了後にオーバーレイ表示）
                    cancellationToken).ConfigureAwait(false);

                // 🚨🚨🚨 [ULTRA_CRITICAL] 呼び出し完了を確実に記録
                var timestamp2 = DateTime.Now.ToString("HH:mm:ss.fff");
                var threadId2 = Environment.CurrentManagedThreadId;
                System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                    $"[{timestamp2}][T{threadId2:D2}] 🚨🚨🚨 [ULTRA_CRITICAL] TranslateBatchWithStreamingAsync呼び出し完了！ - 結果数: {results.Count}\r\n");

                DebugLogUtility.WriteLog($"✅ [PHASE12.2_BATCH] TranslateBatchWithStreamingAsync完了 - 結果数: {results.Count}");
                return results;
            }
            else
            {
                // 通常の翻訳サービスを使用
                DebugLogUtility.WriteLog($"🔥🔥🔥 [PHASE12.2_BATCH] DefaultTranslationService使用（_streamingTranslationService is null）");
                Console.WriteLine($"🔥🔥🔥 [PHASE12.2_BATCH] DefaultTranslationService使用（_streamingTranslationService is null）");
                _logger.LogDebug("🔥 [PHASE12.2] DefaultTranslationService使用");

                var results = new List<string>();
                for (int i = 0; i < batchTexts.Count; i++)
                {
                    var text = batchTexts[i];
                    if (cancellationToken.IsCancellationRequested)
                    {
                        DebugLogUtility.WriteLog($"⚠️ [PHASE12.2_BATCH] キャンセル要求検出 - Index: {i}");
                        break;
                    }

                    DebugLogUtility.WriteLog($"📞📞📞 [PHASE12.2_BATCH] TranslateAsync呼び出し直前 - Index: {i}, Text: '{text}'");
                    Console.WriteLine($"📞📞📞 [PHASE12.2_BATCH] TranslateAsync呼び出し直前 - Index: {i}, Text: '{text}'");

                    var response = await _translationService.TranslateAsync(
                        text,
                        Language.FromCode("ja"), // TODO: 設定から取得
                        Language.FromCode("en"), // TODO: 設定から取得
                        null,
                        cancellationToken).ConfigureAwait(false);

                    DebugLogUtility.WriteLog($"✅✅✅ [PHASE12.2_BATCH] TranslateAsync完了 - Index: {i}, TranslatedText: '{response.TranslatedText}'");
                    Console.WriteLine($"✅✅✅ [PHASE12.2_BATCH] TranslateAsync完了 - Index: {i}, TranslatedText: '{response.TranslatedText}'");

                    results.Add(response.TranslatedText);
                }

                DebugLogUtility.WriteLog($"✅ [PHASE12.2_BATCH] DefaultTranslationService完了 - 結果数: {results.Count}");
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
            DebugLogUtility.WriteLog($"🎯🎯🎯 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync メソッド開始 - ChunkCount: {translatedChunks.Count}");
            Console.WriteLine($"🎯🎯🎯 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync メソッド開始 - ChunkCount: {translatedChunks.Count}");

            _logger.LogInformation("🎯 [PHASE12.2] インプレースオーバーレイ表示開始 - チャンク数: {Count}",
                translatedChunks.Count);

            // 翻訳結果の詳細ログ
            for (int i = 0; i < translatedChunks.Count; i++)
            {
                var chunk = translatedChunks[i];
                DebugLogUtility.WriteLog($"   🔍 [PHASE12.2_OVERLAY] チャンク[{i}]: '{chunk.CombinedText}' → '{chunk.TranslatedText}'");
                _logger.LogDebug("   [{Index}] '{Original}' → '{Translated}'",
                    i, chunk.CombinedText, chunk.TranslatedText);
            }

            // 各チャンクをインプレース表示
            int displayedCount = 0;
            foreach (var chunk in translatedChunks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    DebugLogUtility.WriteLog($"⚠️ [PHASE12.2_OVERLAY] キャンセル要求検出 - 表示中断");
                    break;
                }

                if (chunk.CanShowInPlace() && !string.IsNullOrWhiteSpace(chunk.TranslatedText))
                {
                    DebugLogUtility.WriteLog($"🔥 [PHASE12.2_OVERLAY] ShowInPlaceOverlayAsync実行開始 - ChunkId: {chunk.ChunkId}");
                    _logger.LogDebug("🔥 [PHASE12.2] ShowInPlaceOverlayAsync実行 - ChunkId: {ChunkId}",
                        chunk.ChunkId);

                    await _overlayManager.ShowInPlaceOverlayAsync(chunk).ConfigureAwait(false);

                    displayedCount++;
                    DebugLogUtility.WriteLog($"   ✅ [PHASE12.2_OVERLAY] ShowInPlaceOverlayAsync完了 - ChunkId: {chunk.ChunkId}, 累計表示: {displayedCount}個");
                    _logger.LogDebug("   ✅ [PHASE12.2] インプレース表示完了 - ChunkId: {ChunkId}",
                        chunk.ChunkId);
                }
                else
                {
                    DebugLogUtility.WriteLog($"⚠️ [PHASE12.2_OVERLAY] スキップ - ChunkId: {chunk.ChunkId}, CanShowInPlace: {chunk.CanShowInPlace()}, HasTranslation: {!string.IsNullOrWhiteSpace(chunk.TranslatedText)}");
                }
            }

            DebugLogUtility.WriteLog($"🎉🎉🎉 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync完了 - 表示数: {displayedCount}/{translatedChunks.Count}");
            Console.WriteLine($"🎉🎉🎉 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync完了 - 表示数: {displayedCount}/{translatedChunks.Count}");

            _logger.LogInformation("🎉 [PHASE12.2] 座標ベース翻訳処理完了 - オーバーレイ表示成功");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌❌❌ [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync例外: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"❌❌❌ [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync例外: {ex.GetType().Name} - {ex.Message}");
            _logger.LogError(ex, "❌ [PHASE12.2] オーバーレイ表示エラー");
            throw;
        }
    }
}
