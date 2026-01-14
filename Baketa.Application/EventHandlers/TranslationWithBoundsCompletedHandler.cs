using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.UI.Overlays;
using Baketa.Core.Events.EventTypes;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.EventHandlers;

/// <summary>
/// 座標情報付き翻訳完了イベントハンドラー
/// 翻訳完了後にオーバーレイ表示を行う
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="eventAggregator">イベント集約インスタンス</param>
/// <param name="overlayManager">統一オーバーレイマネージャー（OVERLAY_UNIFICATION）</param>
/// <param name="logger">ロガー</param>
public class TranslationWithBoundsCompletedHandler(
    IEventAggregator eventAggregator,
    // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
    IOverlayManager? overlayManager,
    ILogger<TranslationWithBoundsCompletedHandler> logger) : IEventProcessor<TranslationWithBoundsCompletedEvent>
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly IOverlayManager? _overlayManager = overlayManager;
    private readonly ILogger<TranslationWithBoundsCompletedHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

#if DEBUG
    // 🔥🔥🔥 [DEBUG] コンストラクタで型情報をログ出力
    static TranslationWithBoundsCompletedHandler()
    {
        var logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var log = $"[{timestamp}] 🔥🔥🔥 [DEBUG] TranslationWithBoundsCompletedHandler static constructor{Environment.NewLine}";
        try
        {
            File.AppendAllText(logFilePath, log);
        }
        catch { /* ignore */ }
    }
#endif

    // インスタンス初期化時のログ
    private readonly string _instanceId = LogConstructorInfo(overlayManager);

    /// <inheritdoc />
    public int Priority => 200;

    /// <inheritdoc />
    public bool SynchronousExecution => true; // 🔥 [PHASE4.5_FIX] Task.Runのfire-and-forget問題を回避

    /// <inheritdoc />
    public async Task HandleAsync(TranslationWithBoundsCompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        // NULLチェック
        ArgumentNullException.ThrowIfNull(eventData);

        // 🎯 [GROUP_TRANSLATION_RESULT] グループ翻訳結果の詳細ログ
        _logger.LogInformation("🎯 [GROUP_TRANSLATION_RESULT] 翻訳完了 - ID: {EventId}, エンジン: {Engine}",
            eventData.Id, eventData.EngineName);

        _logger.LogInformation("🎯 [GROUP_TRANSLATION_RESULT] 原文: '{SourceText}' ({SourceLang}) → 訳文: '{TranslatedText}' ({TargetLang})",
            eventData.SourceText.Length > 50 ? eventData.SourceText[..50] + "..." : eventData.SourceText,
            eventData.SourceLanguage,
            eventData.TranslatedText.Length > 50 ? eventData.TranslatedText[..50] + "..." : eventData.TranslatedText,
            eventData.TargetLanguage);

        _logger.LogInformation("🎯 [GROUP_TRANSLATION_RESULT] 座標: Rect: ({X},{Y},{W}x{H}), 文字数: {SourceLen} → {TargetLen}",
            eventData.Bounds.X, eventData.Bounds.Y, eventData.Bounds.Width, eventData.Bounds.Height,
            eventData.SourceText.Length, eventData.TranslatedText.Length);

        Console.WriteLine($"🎯 [GROUP_TRANSLATION_RESULT] 翻訳完了 - " +
            $"原文: '{(eventData.SourceText.Length > 30 ? eventData.SourceText[..30] + "..." : eventData.SourceText)}' → " +
            $"訳文: '{(eventData.TranslatedText.Length > 30 ? eventData.TranslatedText[..30] + "..." : eventData.TranslatedText)}'");

        Console.WriteLine($"🎯 [GROUP_TRANSLATION_RESULT] エンジン: {eventData.EngineName}, " +
            $"座標: Rect: ({eventData.Bounds.X},{eventData.Bounds.Y},{eventData.Bounds.Width}x{eventData.Bounds.Height})");

        // 🔧 [PHASE4.5_DEBUG] tryブロック直前の診断ログ

        try
        {
            _logger.LogInformation("座標情報付き翻訳完了: '{Original}' → '{Translated}' (Bounds: {Bounds})",
                eventData.SourceText, eventData.TranslatedText, eventData.Bounds);

            // 🔍 翻訳成功判定：空文字や空白文字の場合は翻訳失敗とみなす
            var isTranslationSuccessful = !string.IsNullOrWhiteSpace(eventData.TranslatedText);

            // 🎯 [COORDINATE_FIX] 座標が(0,0,0,0)でも翻訳テキストが有効なら成功とみなす
            var hasValidBounds = eventData.Bounds.Width > 0 && eventData.Bounds.Height > 0;

            _logger.LogInformation("🎯 [COORDINATE_DEBUG] Bounds: ({X},{Y},{W}x{H}), HasValidBounds: {HasValidBounds}, IsTranslationSuccessful: {IsTranslationSuccessful}",
                eventData.Bounds.X, eventData.Bounds.Y, eventData.Bounds.Width, eventData.Bounds.Height, hasValidBounds, isTranslationSuccessful);

            // 🏗️ PHASE18: 統一オーバーレイシステムを使用（利用可能な場合）
            if (_overlayManager != null && isTranslationSuccessful)
            {
                Console.WriteLine($"🔥 [CRITICAL_DEBUG] ★★★ ifブロック内開始！ ★★★ - ID: {eventData.Id}");
                Console.WriteLine($"🔥 [CRITICAL_DEBUG] IsFallbackTranslation = {eventData.IsFallbackTranslation} - ID: {eventData.Id}");

                // 🔥 [FALLBACK_FIX] フォールバック翻訳の場合、オーバーレイ表示前に既存の個別翻訳オーバーレイを削除
                if (eventData.IsFallbackTranslation)
                {
                    _logger.LogInformation("🧹 [FALLBACK] フォールバック翻訳実行 - 個別翻訳オーバーレイを削除");
                    Console.WriteLine("🧹 [FALLBACK] 個別翻訳オーバーレイを削除 - 全画面翻訳のみ表示");

                    try
                    {
                        // 🔧 [OVERLAY_UNIFICATION] HideAllInPlaceOverlaysAsync() → HideAllAsync() に統一
                        await _overlayManager.HideAllAsync().ConfigureAwait(false);
                        _logger.LogInformation("✅ [FALLBACK] オーバーレイクリア完了");
                    }
                    catch (Exception clearEx)
                    {
                        _logger.LogError(clearEx, "❌ [FALLBACK] オーバーレイクリア失敗");
                    }
                }

                _logger.LogDebug("🚀 [PHASE18_HANDLER] 統一InPlaceTranslationOverlayManager使用開始 - ID: {Id}, IsFallback: {IsFallback}",
                    eventData.Id, eventData.IsFallbackTranslation);

                Console.WriteLine($"🚀 [PHASE18_HANDLER] 統一InPlaceTranslationOverlayManager使用 - EventId: {eventData.Id}, IsFallback: {eventData.IsFallbackTranslation}");

                try
                {
                    // TextChunkを作成（eventDataから）
                    var textChunk = new TextChunk
                    {
                        ChunkId = eventData.Id.GetHashCode(), // Guidからintのハッシュコードを生成
                        CombinedText = eventData.SourceText,
                        TranslatedText = eventData.TranslatedText,
                        CombinedBounds = hasValidBounds ? eventData.Bounds : new System.Drawing.Rectangle(100, 100, 400, 50), // 座標なしの場合は固定位置を使用
                        SourceWindowHandle = IntPtr.Zero, // TranslationWithBoundsCompletedEventにはWindowHandle情報がない
                        DetectedLanguage = eventData.SourceLanguage,
                        TextResults = [] // 最小限のTextChunk作成
                    };

                    Console.WriteLine($"🎯 [COORDINATE_FIX] TextChunk作成 - OriginalBounds: ({eventData.Bounds.X},{eventData.Bounds.Y},{eventData.Bounds.Width}x{eventData.Bounds.Height}), UsedBounds: ({textChunk.CombinedBounds.X},{textChunk.CombinedBounds.Y},{textChunk.CombinedBounds.Width}x{textChunk.CombinedBounds.Height})");

                    // 🔥🔥🔥 [ULTRATHINK_PHASE3] メソッド実体の詳細情報ログ
                    var overlayManagerType = _overlayManager.GetType();
                    var assemblyLocation = overlayManagerType.Assembly.Location;
                    var assemblyLastWriteTime = System.IO.File.GetLastWriteTime(assemblyLocation);

                    Console.WriteLine($"🔥🔥🔥 [OVERLAY_UNIFICATION] Calling ShowAsync on {overlayManagerType.FullName}");
                    Console.WriteLine($"🔥🔥🔥🔥🔥 [ULTRATHINK_PHASE4_ASSEMBLY] Loaded from: {assemblyLocation} (Modified: {assemblyLastWriteTime:HH:mm:ss})");

                    // 🔧 [OVERLAY_UNIFICATION] IOverlayManager統一インターフェースで処理
                    try
                    {
                        Console.WriteLine($"🔥🔥🔥 [OVERLAY_UNIFICATION] try block開始");

                        // OverlayContentの作成
                        var content = new Baketa.Core.Abstractions.UI.Overlays.OverlayContent
                        {
                            Text = eventData.TranslatedText,
                            OriginalText = eventData.SourceText
                        };

                        // OverlayPositionの作成
                        var position = new Baketa.Core.Abstractions.UI.Overlays.OverlayPosition
                        {
                            X = eventData.Bounds.X,
                            Y = eventData.Bounds.Y,
                            Width = eventData.Bounds.Width,
                            Height = eventData.Bounds.Height
                        };

                        // 統一IOverlayManager.ShowAsync()でオーバーレイ表示
                        await _overlayManager.ShowAsync(content, position).ConfigureAwait(false);

                        Console.WriteLine($"🔥🔥🔥 [OVERLAY_UNIFICATION] ShowAsync正常完了");
                    }
                    catch (Exception innerEx)
                    {
                        Console.WriteLine($"🔥🔥🔥 [OVERLAY_UNIFICATION] ShowAsync内部で例外: {innerEx.GetType().Name} - {innerEx.Message}");
                        throw;
                    }

                    _logger.LogInformation("✅ [PHASE18_HANDLER] 統一システムでオーバーレイ表示成功 - ID: {Id}, Text: '{Text}'",
                        eventData.Id, eventData.TranslatedText.Substring(0, Math.Min(30, eventData.TranslatedText.Length)));

                    // 🎯 [OVERLAY_COORDINATES] オーバーレイ座標ログ追加
                    _logger.LogInformation("🎯 [OVERLAY_COORDINATES] 統一システムオーバーレイ座標: Rect: ({X},{Y},{W}x{H})",
                        eventData.Bounds.X, eventData.Bounds.Y, eventData.Bounds.Width, eventData.Bounds.Height);
                    Console.WriteLine($"🎯 [OVERLAY_COORDINATES] 統一システムオーバーレイ座標: Rect: ({eventData.Bounds.X},{eventData.Bounds.Y},{eventData.Bounds.Width}x{eventData.Bounds.Height})");
                    Console.WriteLine($"✅ [PHASE18_HANDLER] 統一システム表示成功 - ID: {eventData.Id}");

                    // 翻訳結果表示成功 - ローディング終了イベントを発行
                    var loadingEndEvent = new FirstTranslationResultReceivedEvent();
                    _logger.LogWarning("✅ [LOADING_END] 翻訳結果表示成功 - FirstTranslationResultReceivedEvent発行開始 ID: {EventId}, 型: {EventType}",
                        loadingEndEvent.Id, loadingEndEvent.GetType().FullName);
                    await _eventAggregator.PublishAsync(loadingEndEvent).ConfigureAwait(false);
                    _logger.LogWarning("✅ [LOADING_END] FirstTranslationResultReceivedEvent発行完了");

                    // ✅ [DUPLICATE_FIX] 統一システム成功時はLegacyシステムをスキップ
                    Console.WriteLine($"🚫 [DUPLICATE_FIX] 統一システム成功のため既存システムスキップ - ID: {eventData.Id}");
                    return; // 統一システム成功時は処理完了
                }
                catch (Exception overlayManagerEx)
                {
                    _logger.LogError(overlayManagerEx, "❌ [PHASE18_HANDLER] 統一オーバーレイマネージャー処理中にエラー発生 - ID: {Id}", eventData.Id);
                    Console.WriteLine($"❌ [PHASE18_HANDLER] 統一システムエラー - ID: {eventData.Id}");

                    // 統一システムでエラーが発生した場合は既存システムにフォールバック
                    _logger.LogWarning("⚠️ [PHASE18_HANDLER] 既存システムにフォールバック実行");
                    await PublishLegacyOverlayEvent();
                }
            }
            else
            {
                Console.WriteLine($"🔥 [CRITICAL_DEBUG] ▼▼▼ elseブロック開始！ ▼▼▼ - ID: {eventData.Id}");

                // 既存システムを使用（統一システム無効 or 翻訳失敗）
                _logger.LogDebug("🔄 [LEGACY_HANDLER] 既存システム使用 - OverlayManager: {HasManager}, Success: {Success}",
                    _overlayManager != null, isTranslationSuccessful);
                await PublishLegacyOverlayEvent();
            }

            async Task PublishLegacyOverlayEvent()
            {
                // 🔍 [DEBUG] オーバーレイソース特定と翻訳成功判定
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] TranslationWithBoundsCompletedHandler → OverlayUpdateEvent発行");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] EventId: {eventData.Id}");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] SourceText: '{eventData.SourceText}'");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] TranslatedText: '{eventData.TranslatedText}'");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] SourceLanguage: {eventData.SourceLanguage}");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] TargetLanguage: {eventData.TargetLanguage}");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] EngineName: {eventData.EngineName}");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] Bounds: {eventData.Bounds}");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] IsTranslationSuccessful: {isTranslationSuccessful}");

                // 🎯 [OVERLAY_COORDINATES] Legacy既存システムオーバーレイ座標ログ追加
                _logger.LogInformation("🎯 [OVERLAY_COORDINATES] 既存システムオーバーレイ座標: Rect: ({X},{Y},{W}x{H})",
                    eventData.Bounds.X, eventData.Bounds.Y, eventData.Bounds.Width, eventData.Bounds.Height);
                Console.WriteLine($"🎯 [OVERLAY_COORDINATES] 既存システムオーバーレイ座標: Rect: ({eventData.Bounds.X},{eventData.Bounds.Y},{eventData.Bounds.Width}x{eventData.Bounds.Height})");

                // オーバーレイ更新イベントを発行
                var overlayEvent = new OverlayUpdateEvent(
                    text: eventData.TranslatedText,
                    displayArea: eventData.Bounds,
                    originalText: eventData.SourceText,
                    sourceLanguage: eventData.SourceLanguage,
                    targetLanguage: eventData.TargetLanguage,
                    isTranslationResult: isTranslationSuccessful);

                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] OverlayUpdateEvent発行中 - ID: {overlayEvent.Id}");
                await _eventAggregator.PublishAsync(overlayEvent).ConfigureAwait(false);
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] OverlayUpdateEvent発行完了 - ID: {overlayEvent.Id}");
            }

            // 翻訳成功通知
            var notificationEvent = new NotificationEvent(
                $"翻訳完了: {eventData.EngineName}",
                NotificationType.Success,
                "翻訳",
                displayTime: 2000);

            await _eventAggregator.PublishAsync(notificationEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 🔧 [PHASE4.5_DEBUG] 例外catchブロック - 必ず出力
            var exceptionMessage = $"💥 [PHASE4.5_DEBUG] 例外発生! Type: {ex.GetType().Name}, Message: {ex.Message}, StackTrace: {ex.StackTrace}";
            Console.WriteLine(exceptionMessage);

            _logger.LogError(ex, "座標情報付き翻訳完了イベント処理中にエラーが発生: '{Text}'", eventData.SourceText);
        }
    }

    /// <summary>
    /// ログファイルに直接出力
    /// </summary>
    // ✅ [P1-A_FIX] File.AppendAllTextAsync()によるファイルロック競合を解消するため削除
    // デバッグログは_logger.LogDebug()で既に出力されているため、情報損失なし
    // CLAUDE.mdロギング標準に準拠
    private async Task WriteToLogFileAsync(string message)
    {
        // Method removed - use ILogger instead
        await Task.CompletedTask;
    }

    // 🔥🔥🔥 [DEBUG] インスタンス初期化時の型情報ログ
    // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
    private static string LogConstructorInfo(IOverlayManager? overlayManager)
    {
        var instanceId = Guid.NewGuid().ToString("N")[..8];
#if DEBUG
        var typeName = overlayManager?.GetType().FullName ?? "NULL";
        var logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var log1 = $"[{timestamp}] 🔥🔥🔥 [DEBUG] TranslationWithBoundsCompletedHandler インスタンス作成 - ID: {instanceId}{Environment.NewLine}";
        var log2 = $"[{timestamp}] 🔥🔥🔥 [DEBUG] _overlayManager実際の型: {typeName}{Environment.NewLine}";
        try
        {
            File.AppendAllText(logFilePath, log1 + log2);
        }
        catch { /* ignore */ }
        Console.WriteLine($"🔥🔥🔥 [DEBUG] TranslationWithBoundsCompletedHandler - ID: {instanceId}, Type: {typeName}");
#endif
        return instanceId;
    }
}
