using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.Translation;
using Baketa.Core.Settings;
using Baketa.Core.Utilities;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// 時間軸ベースのTextChunk集約処理クラス
/// OCR結果を一定時間バッファリングし、統合してから翻訳パイプラインに送信
/// 戦略書設計: translation-quality-improvement-strategy.md 完全準拠実装
/// </summary>
public sealed class TimedChunkAggregator : IDisposable
{
    private readonly System.Threading.Timer _aggregationTimer;
    private readonly ConcurrentDictionary<IntPtr, List<TextChunk>> _pendingChunksByWindow;
    private readonly SemaphoreSlim _processingLock;
    private readonly ILogger<TimedChunkAggregator> _logger;
    private readonly CoordinateBasedLineBreakProcessor _lineBreakProcessor;
    private readonly ICoordinateTransformationService _coordinateTransformationService;
    private readonly ProximityGroupingService _proximityGroupingService;
    private readonly IEventAggregator _eventAggregator;

    // 設定可能なバッファ時間
    private readonly IOptionsMonitor<TimedAggregatorSettings> _settings;
    private readonly IDisposable? _settingsChangeSubscription;

    // パフォーマンス監視用
    private long _totalChunksProcessed;
    private long _totalAggregationEvents;
    private readonly System.Diagnostics.Stopwatch _performanceStopwatch;
    private DateTime _lastTimerReset;
    private volatile int _nextChunkId;

    public TimedChunkAggregator(
        IOptionsMonitor<TimedAggregatorSettings> settings,
        CoordinateBasedLineBreakProcessor lineBreakProcessor,
        ICoordinateTransformationService coordinateTransformationService,
        ProximityGroupingService proximityGroupingService,
        IEventAggregator eventAggregator,
        ILogger<TimedChunkAggregator> logger)
    {
        // 引数バリデーション（logger を最初に設定）
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lineBreakProcessor = lineBreakProcessor ?? throw new ArgumentNullException(nameof(lineBreakProcessor));
        _coordinateTransformationService = coordinateTransformationService ?? throw new ArgumentNullException(nameof(coordinateTransformationService));
        _proximityGroupingService = proximityGroupingService ?? throw new ArgumentNullException(nameof(proximityGroupingService));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        
        // 🔍 設定デバッグ情報出力
        _logger.LogDebug("🔍 [CONFIG_DEBUG] TimedChunkAggregator設定デバッグ開始");
        _logger.LogDebug("🔍 [CONFIG_DEBUG] settings parameter: {IsNull}", settings == null ? "NULL" : "NOT NULL");
        
        if (settings != null)
        {
            _logger.LogDebug("🔍 [CONFIG_DEBUG] settings.CurrentValue: {IsNull}", settings.CurrentValue == null ? "NULL" : "NOT NULL");
            if (settings.CurrentValue != null)
            {
                _logger.LogDebug("🔍 [CONFIG_DEBUG] settings.CurrentValue.IsFeatureEnabled: {Enabled}", settings.CurrentValue.IsFeatureEnabled);
                _logger.LogDebug("🔍 [CONFIG_DEBUG] settings.CurrentValue.BufferDelayMs: {DelayMs}", settings.CurrentValue.BufferDelayMs);
                _logger.LogDebug("🔍 [CONFIG_DEBUG] settings.CurrentValue.ProximityGrouping.Enabled: {ProximityEnabled}", settings.CurrentValue.ProximityGrouping.Enabled);
                _logger.LogDebug("🔍 [CONFIG_DEBUG] settings.CurrentValue.ProximityGrouping.VerticalDistanceFactor: {VFactor}", settings.CurrentValue.ProximityGrouping.VerticalDistanceFactor);
                _logger.LogDebug("🔍 [CONFIG_DEBUG] settings.CurrentValue.ProximityGrouping.HorizontalDistanceFactor: {HFactor}", settings.CurrentValue.ProximityGrouping.HorizontalDistanceFactor);
            }
        }
        
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // 設定変更の動的反映を購読
        _settingsChangeSubscription = _settings.OnChange((newSettings, _) =>
        {
            _logger.LogInformation("⚙️ 設定変更を検出 - 動的反映開始");
            _logger.LogDebug("⚙️ 新しい設定 - IsFeatureEnabled: {Enabled}, BufferDelayMs: {DelayMs}, ProximityGrouping.Enabled: {ProximityEnabled}",
                newSettings.IsFeatureEnabled, newSettings.BufferDelayMs, newSettings.ProximityGrouping.Enabled);
        });
        
        // フォールバック後の設定値も確認
        _logger.LogDebug("🔍 [CONFIG_DEBUG] Final _settings.CurrentValue.IsFeatureEnabled: {Enabled}", _settings.CurrentValue.IsFeatureEnabled);
        _logger.LogDebug("🔍 [CONFIG_DEBUG] Final _settings.CurrentValue.BufferDelayMs: {DelayMs}", _settings.CurrentValue.BufferDelayMs);
        _logger.LogDebug("🔍 [CONFIG_DEBUG] TimedAggregatorSettings.Development.IsFeatureEnabled: {DevEnabled}", TimedAggregatorSettings.Development.IsFeatureEnabled);
        
        _pendingChunksByWindow = new ConcurrentDictionary<IntPtr, List<TextChunk>>();
        _processingLock = new SemaphoreSlim(1, 1);
        _performanceStopwatch = new System.Diagnostics.Stopwatch();
        _lastTimerReset = DateTime.UtcNow;
        _nextChunkId = Random.Shared.Next(1000000, 9999999);

        // ProximityGroupingService設定適用
        var proximitySettings = _settings.CurrentValue.ProximityGrouping;
        // TODO: ProximityGroupingServiceに設定適用メソッドを追加予定

        _aggregationTimer = new System.Threading.Timer(ProcessPendingChunks, null,
            Timeout.Infinite, Timeout.Infinite);

        _logger.LogInformation("🧩 TimedChunkAggregator初期化完了 - " +
            "BufferDelay: {DelayMs}ms, Feature: {Enabled}, " +
            "ProximityGrouping: {ProximityEnabled}",
            _settings.CurrentValue.BufferDelayMs, _settings.CurrentValue.IsFeatureEnabled, proximitySettings.Enabled);
    }

    /// <summary>
    /// TimedAggregator機能が有効かどうかを示すプロパティ
    /// CoordinateBasedTranslationServiceの重複制御で使用
    /// </summary>
    /// <remarks>
    /// 🚀 [DUPLICATE_FIX] アプローチ2.5による重複解消修正
    /// Gemini専門家レビュー承認済みの実装
    /// </remarks>
    public bool IsFeatureEnabled => _settings.CurrentValue.IsFeatureEnabled;

    /// <summary>
    /// 新しいチャンクを追加し、タイマーをリセット
    /// 戦略書フィードバック反映: SourceWindowHandle別管理、ForceFlushMs制御
    /// </summary>
    public async Task<bool> TryAddChunkAsync(TextChunk chunk, CancellationToken cancellationToken = default)
    {
        // 🔥 [TIMED_AGG_ENTRY] メソッド最上部診断
        Console.WriteLine($"🔥🔥🔥 [TIMED_AGG_ENTRY] TryAddChunkAsync実行開始 - ChunkId: {chunk.ChunkId}");
        _logger?.LogDebug(
            $"🔥🔥🔥 [TIMED_AGG_ENTRY] TryAddChunkAsync実行開始 - " +
            $"ChunkId: {chunk.ChunkId}, Feature Enabled: {_settings.CurrentValue.IsFeatureEnabled}"
        );
        _logger.LogCritical(
            "🔥🔥🔥 [TIMED_AGG_ENTRY] TryAddChunkAsync実行開始 - " +
            "ChunkId: {ChunkId}, Feature Enabled: {Enabled}",
            chunk.ChunkId,
            _settings.CurrentValue.IsFeatureEnabled
        );

        // Feature Flag チェック - 機能が無効の場合は即座にfalseを返す
        if (!_settings.CurrentValue.IsFeatureEnabled)
        {
            Console.WriteLine("🔥 [TIMED_AGG_DISABLED] Feature無効により早期リターン");
            _logger?.LogDebug("🔥 [TIMED_AGG_DISABLED] Feature無効により早期リターン");
            _logger.LogCritical("🔥 [TIMED_AGG_DISABLED] Feature無効により早期リターン");
            _logger.LogDebug("TimedChunkAggregator機能が無効化されています");
            return false;
        }

        Console.WriteLine("🔥 [TIMED_AGG_LOCK_BEFORE] ロック取得試行前");
        _logger?.LogDebug("🔥 [TIMED_AGG_LOCK_BEFORE] ロック取得試行前");
        _logger.LogCritical("🔥 [TIMED_AGG_LOCK_BEFORE] ロック取得試行前");

        _logger.LogDebug("🔐 [PHASE_C_DEBUG] TryAddChunkAsync開始 - ロック取得試行中");
        await _processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        Console.WriteLine("🔥 [TIMED_AGG_LOCK_AFTER] ロック取得成功");
        _logger?.LogDebug("🔥 [TIMED_AGG_LOCK_AFTER] ロック取得成功");
        _logger.LogCritical("🔥 [TIMED_AGG_LOCK_AFTER] ロック取得成功");
        _logger.LogDebug("✅ [PHASE_C_DEBUG] ロック取得成功 - 処理開始");
        try
        {
            // パフォーマンス計測開始
            _performanceStopwatch.Start();

            // 🔍 Phase 20: 追加されるチャンクの内容をログ出力
            var chunkText = chunk.CombinedText ?? chunk.TextResults?.FirstOrDefault()?.Text ?? "";
            _logger.LogInformation("📥 [Phase20] チャンク追加: ID:{ChunkId}, Text:「{Text}」",
                chunk.ChunkId, chunkText.Length > 100 ? chunkText[..100] + "..." : chunkText);

            // SourceWindowHandle別にバッファを分離（コンテキスト混在防止）
            var windowHandle = chunk.SourceWindowHandle;
            if (!_pendingChunksByWindow.TryGetValue(windowHandle, out var existingChunks))
            {
                existingChunks = [];
                _pendingChunksByWindow[windowHandle] = existingChunks;
            }

            existingChunks.Add(chunk);
            Interlocked.Increment(ref _totalChunksProcessed);

            // 🔧 [PHASE3.4B] 最初のチャンク追加時にタイマーリセット時刻を更新（ForceFlushMs誤検知防止）
            // 各ウィンドウの最初のチャンク追加時に_lastTimerResetを更新することで、
            // 前回のOCRセッションからの経過時間蓄積による誤検知を防止
            if (existingChunks.Count == 1)
            {
                _lastTimerReset = DateTime.UtcNow;
                _logger.LogDebug("🔧 [PHASE3.4B] 最初のチャンク追加 - タイマーリセット時刻更新 (ウィンドウ: {WindowHandle})", windowHandle);
            }

            // 全ウィンドウのチャンク数を計算
            var totalChunks = _pendingChunksByWindow.Values.Sum(list => list.Count);

            // 🔥 [PHASE12.2_TIMER_DEBUG] タイマー制御分岐診断
            Console.WriteLine($"🔥 [PHASE12.2_TIMER_DEBUG] totalChunks: {totalChunks}, MaxChunkCount: {_settings.CurrentValue.MaxChunkCount}");
            _logger?.LogDebug($"🔥 [PHASE12.2_TIMER_DEBUG] totalChunks: {totalChunks}, MaxChunkCount: {_settings.CurrentValue.MaxChunkCount}");

            // メモリ保護：最大チャンク数を超えたら強制処理
            if (totalChunks >= _settings.CurrentValue.MaxChunkCount)
            {
                _logger.LogWarning("⚠️ [Phase20] 最大チャンク数到達 - 強制処理開始: {Count}個 (設定値: {MaxCount})",
                    totalChunks, _settings.CurrentValue.MaxChunkCount);
                await ProcessPendingChunksInternal().ConfigureAwait(false);
                return true;
            }

            // ForceFlushMs制御: 無限タイマーリセットを防ぐ
            var timeSinceLastReset = DateTime.UtcNow - _lastTimerReset;

            // 🔥 [PHASE12.2_TIMER_DEBUG] ForceFlushMs判定前の診断
            Console.WriteLine($"🔥 [PHASE12.2_TIMER_DEBUG] timeSinceLastReset: {timeSinceLastReset.TotalMilliseconds}ms, ForceFlushMs: {_settings.CurrentValue.ForceFlushMs}ms");
            _logger?.LogDebug($"🔥 [PHASE12.2_TIMER_DEBUG] timeSinceLastReset: {timeSinceLastReset.TotalMilliseconds}ms, ForceFlushMs: {_settings.CurrentValue.ForceFlushMs}ms");

            if (timeSinceLastReset.TotalMilliseconds >= _settings.CurrentValue.ForceFlushMs)
            {
                _logger.LogWarning("🚨 [PHASE_20_EMERGENCY] ForceFlushMs到達 - タイマー長期停止検出: {ElapsedMs}ms経過 (設定値: {ForceFlushMs}ms)",
                    timeSinceLastReset.TotalMilliseconds, _settings.CurrentValue.ForceFlushMs);

                // 🚀 Phase 20緊急修正: ForceFlushMs後にタイマーを強制リセット
                try
                {
                    await ProcessPendingChunksInternal().ConfigureAwait(false);

                    // タイマーを強制的に再起動（Phase 20追加）
                    bool emergencyTimerReset = _aggregationTimer.Change(_settings.CurrentValue.BufferDelayMs, Timeout.Infinite);
                    _lastTimerReset = DateTime.UtcNow;

                    _logger.LogInformation("🔧 [PHASE_20_EMERGENCY] 緊急タイマーリセット実行 - 結果: {Result}, {DelayMs}ms後に再開予定",
                        emergencyTimerReset, _settings.CurrentValue.BufferDelayMs);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🚨 [PHASE_20_EMERGENCY] 緊急タイマーリセット失敗");
                }
            }
            else
            {
                // 🔥 [PHASE12.2_TIMER_DEBUG] 通常タイマーリセット処理に到達
                Console.WriteLine("🔥 [PHASE12.2_TIMER_DEBUG] else節到達 - 通常タイマーリセット処理開始");
                _logger?.LogDebug("🔥 [PHASE12.2_TIMER_DEBUG] else節到達 - 通常タイマーリセット処理開始");

                // 🚀 Phase 19緊急修正: タイマー確実実行保証とタイマー状況監視
                try
                {
                    var timerResetStart = DateTime.UtcNow;
                    _logger.LogDebug("🔄 [PHASE_19_FIX] タイマーリセット開始 - DelayMs: {DelayMs}, Current: {CurrentTime}",
                        _settings.CurrentValue.BufferDelayMs, timerResetStart);

                    // 🔥 [PHASE12.2_TIMER_DEBUG] timer.Change()実行直前
                    Console.WriteLine($"🔥 [PHASE12.2_TIMER_DEBUG] timer.Change()呼び出し直前 - DelayMs: {_settings.CurrentValue.BufferDelayMs}ms");
                    _logger?.LogDebug($"🔥 [PHASE12.2_TIMER_DEBUG] timer.Change()呼び出し直前 - DelayMs: {_settings.CurrentValue.BufferDelayMs}ms");

                    // タイマーをリセット（新しいチャンクが来たら待ち時間をリセット）
                    bool timerChangeResult = _aggregationTimer.Change(_settings.CurrentValue.BufferDelayMs, Timeout.Infinite);
                    _lastTimerReset = DateTime.UtcNow; // タイマーリセット時刻を記録

                    // 🔥 [PHASE12.2_TIMER_DEBUG] timer.Change()実行直後
                    Console.WriteLine($"🔥 [PHASE12.2_TIMER_DEBUG] timer.Change()実行完了 - Result: {timerChangeResult}");
                    _logger?.LogDebug($"🔥 [PHASE12.2_TIMER_DEBUG] timer.Change()実行完了 - Result: {timerChangeResult}");

                    _logger.LogInformation("⏱️ [PHASE_19_FIX] タイマーリセット完了 - 結果: {Result}, {DelayMs}ms後に処理予定 (バッファ中: {Count}個)",
                        timerChangeResult, _settings.CurrentValue.BufferDelayMs, totalChunks);

                    // タイマー実行監視用のバックアップタスク（Phase 19安全機構）
                    var expectedFireTime = DateTime.UtcNow.AddMilliseconds(_settings.CurrentValue.BufferDelayMs + 50); // 50ms余裕
                    _ = Task.Delay(_settings.CurrentValue.BufferDelayMs + 100).ContinueWith(async _ =>
                    {
                        try
                        {
                            var now = DateTime.UtcNow;
                            var timeSinceReset = (now - _lastTimerReset).TotalMilliseconds;

                            if (timeSinceReset >= _settings.CurrentValue.BufferDelayMs + 50 && _pendingChunksByWindow.Count > 0)
                            {
                                _logger.LogWarning("🚨 [PHASE_19_BACKUP] タイマー実行遅延検出 - {ElapsedMs}ms経過、バックアップ処理実行",
                                    timeSinceReset);
                                await ProcessPendingChunksInternal().ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "🚨 [PHASE_19_BACKUP] バックアップタイマー処理失敗");
                        }
                    }, TaskScheduler.Default);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🚨 [PHASE_19_FIX] タイマーリセット失敗 - 緊急フォールバック実行");
                    // タイマー失敗時は即座に処理実行
                    await ProcessPendingChunksInternal().ConfigureAwait(false);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "チャンク追加処理中にエラーが発生: ChunkId={ChunkId}, WindowHandle={WindowHandle}", 
                chunk?.ChunkId, chunk?.SourceWindowHandle);
            throw;
        }
        finally
        {
            _performanceStopwatch.Stop();
            _processingLock.Release();
        }
    }

    /// <summary>
    /// バッファされたチャンクを統合処理（タイマーコールバック）
    /// UltraThink Phase A緊急修正: Fire-and-forgetパターン改善とエラーハンドリング強化
    /// </summary>
    private async void ProcessPendingChunks(object? state)
    {
        // 🚨 [CALLBACK_ENTRY] 絶対最初に実行されるログ（例外発生前診断）
        try { Console.WriteLine("🚨🚨🚨 [CALLBACK_ENTRY] ProcessPendingChunks()メソッド開始 - Thread: {0}", Environment.CurrentManagedThreadId); } catch { }
        try { _logger?.LogDebug($"🚨🚨🚨 [CALLBACK_ENTRY] ProcessPendingChunks()メソッド開始 - Thread: {Environment.CurrentManagedThreadId}"); } catch { }

        // 🔥 [PHASE12.2_CALLBACK] タイマーコールバック実行開始
        Console.WriteLine("🔥🔥🔥 [PHASE12.2_CALLBACK] ProcessPendingChunks()タイマーコールバック実行開始");
        _logger?.LogDebug("🔥🔥🔥 [PHASE12.2_CALLBACK] ProcessPendingChunks()タイマーコールバック実行開始");

        // 🚀 Phase 19強化: タイマーコールバック実行状況詳細監視
        var callbackStart = DateTime.UtcNow;
        var timeSinceLastReset = (callbackStart - _lastTimerReset).TotalMilliseconds;

        Console.WriteLine($"🔥 [PHASE12.2_CALLBACK] timeSinceLastReset: {timeSinceLastReset}ms, 期待値: {_settings.CurrentValue.BufferDelayMs}ms");
        _logger?.LogDebug($"🔥 [PHASE12.2_CALLBACK] timeSinceLastReset: {timeSinceLastReset}ms, 期待値: {_settings.CurrentValue.BufferDelayMs}ms");

        _logger.LogInformation("🔥 [PHASE_19_CALLBACK] タイマーコールバック実行開始 - リセットから{ElapsedMs}ms経過, 期待値: {ExpectedMs}ms",
            timeSinceLastReset, _settings.CurrentValue.BufferDelayMs);

        try
        {
            _logger.LogDebug("🔄 [PHASE_C_FIX] タイマーコールバック実行開始");
            await ProcessPendingChunksInternal().ConfigureAwait(false);

            var processingTime = (DateTime.UtcNow - callbackStart).TotalMilliseconds;
            _logger.LogInformation("✅ [PHASE_19_CALLBACK] タイマーコールバック正常完了 - 処理時間: {ProcessingMs}ms", processingTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 [PHASE_C_FIX] タイマーコールバック実行失敗 - 緊急フォールバック処理実行");

            // 🛡️ 緊急フォールバック: 直接OnChunksAggregatedを呼び出す
            try
            {
                await ExecuteFallbackProcessing().ConfigureAwait(false);
                _logger.LogInformation("🔧 [PHASE_C_FIX] フォールバック処理成功 - 翻訳パイプライン復旧");
            }
            catch (Exception fallbackEx)
            {
                _logger.LogCritical(fallbackEx, "💥 [PHASE_C_FIX] フォールバック処理も失敗 - 緊急対応が必要");
            }
        }
    }

    /// <summary>
    /// バッファされたチャンクを統合処理（非同期実装）
    /// UltraThink Phase A緊急修正: SemaphoreLock競合回避とフォールバック処理追加
    /// </summary>
    private async Task ProcessPendingChunksAsync()
    {
        // 🚀 Phase A緊急修正: 短いタイムアウト + フォールバック処理でSemaphoreLock競合回避
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        try
        {
            if (!await _processingLock.WaitAsync(100, cts.Token).ConfigureAwait(false))
            {
                _logger.LogWarning("⚠️ [PHASE_A_FIX] SemaphoreLock競合検出 - 即座にフォールバック実行 (タイムアウト: 100ms)");

                // 🛡️ 即座にフォールバック処理実行
                await ExecuteFallbackProcessing().ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("⚠️ [PHASE_A_FIX] ProcessPendingChunksAsync全体がタイムアウト - フォールバック実行");
            await ExecuteFallbackProcessing().ConfigureAwait(false);
            return;
        }

        try
        {
            await ProcessPendingChunksInternal().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // async Task内での例外は適切にログ出力（アプリケーション安定性向上）
            _logger.LogError(ex, "🚨 ProcessPendingChunksAsyncでハンドルされない例外が発生しました。");
        }
        finally
        {
            _processingLock.Release();
        }
    }

    /// <summary>
    /// 緊急フォールバック処理
    /// UltraThink Phase A緊急修正: SemaphoreLock競合時の代替処理
    /// </summary>
    private async Task ExecuteFallbackProcessing()
    {
        try
        {
            _logger.LogInformation("🔧 [PHASE_A_FIX] 緊急フォールバック処理開始 - ロックバイパス実行");

            // ロックを取得せずに現在のチャンクを読み取り専用で処理
            var allChunks = new List<TextChunk>();

            // 各ウィンドウのチャンクを安全にコピー（ロックなしで読み取り専用アクセス）
            foreach (var kvp in _pendingChunksByWindow.ToList())
            {
                var windowHandle = kvp.Key;
                var chunks = kvp.Value?.ToList() ?? [];

                if (chunks.Count > 0)
                {
                    allChunks.AddRange(chunks);
                    _logger.LogDebug("📦 [PHASE_A_FIX] フォールバック: ウィンドウ {WindowHandle} から {Count}個のチャンク取得",
                        windowHandle, chunks.Count);
                }
            }

            if (allChunks.Count > 0)
            {
                // 簡易統合（CoordinateBasedLineBreakProcessorを使用せず基本的な結合）
                var combinedText = string.Join(" ", allChunks.Select(c => c.CombinedText ?? "").Where(t => !string.IsNullOrWhiteSpace(t)));

                if (!string.IsNullOrWhiteSpace(combinedText))
                {
                    // 代表チャンクを作成
                    var fallbackChunk = new TextChunk
                    {
                        ChunkId = GenerateNewChunkId(),
                        CombinedText = combinedText,
                        CombinedBounds = allChunks.First().CombinedBounds,
                        SourceWindowHandle = allChunks.First().SourceWindowHandle,
                        DetectedLanguage = allChunks.First().DetectedLanguage,
                        TextResults = allChunks.SelectMany(c => c.TextResults).ToList()
                    };

                    // 🔥 [PHASE12.2] AggregatedChunksReadyEvent発行（旧コールバック削除済み）
                    var aggregatedEvent = new AggregatedChunksReadyEvent(
                        new List<TextChunk> { fallbackChunk }.AsReadOnly(),
                        fallbackChunk.SourceWindowHandle
                    );

                    await _eventAggregator.PublishAsync(aggregatedEvent).ConfigureAwait(false);
                    _logger.LogInformation("✅ [PHASE_A_FIX] フォールバック処理成功 - AggregatedChunksReadyEvent発行完了 (テキスト長: {Length})",
                        combinedText.Length);

                    // 統計更新
                    Interlocked.Increment(ref _totalAggregationEvents);
                }
                else
                {
                    _logger.LogWarning("⚠️ [PHASE_A_FIX] フォールバック: 統合可能テキストなし");
                }
            }
            else
            {
                _logger.LogDebug("📭 [PHASE_A_FIX] フォールバック: 処理対象チャンクなし");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE_A_FIX] 緊急フォールバック処理でエラー発生");
            throw;
        }
    }

    /// <summary>
    /// 内部統合処理
    /// 戦略書フィードバック反映: ウィンドウハンドル別処理
    /// </summary>
    private async Task ProcessPendingChunksInternal()
    {
        // 🔥 [PHASE12.2_INTERNAL] ProcessPendingChunksInternal実行開始
        Console.WriteLine("🔥🔥🔥 [PHASE12.2_INTERNAL] ProcessPendingChunksInternal実行開始");
        _logger?.LogDebug("🔥🔥🔥 [PHASE12.2_INTERNAL] ProcessPendingChunksInternal実行開始");
        _logger.LogCritical("🔥🔥🔥 [PHASE12.2_INTERNAL] ProcessPendingChunksInternal実行開始");

        // 🚨 [STACK_TRACE] 呼び出し元特定のためスタックトレースをログ出力
        var stackTrace = new System.Diagnostics.StackTrace(1, true); // 1フレームスキップ（自身を除く）
        Console.WriteLine($"🚨🚨🚨 [STACK_TRACE] 呼び出し元:\n{stackTrace}");
        _logger?.LogDebug($"🚨🚨🚨 [STACK_TRACE] 呼び出し元:\n{stackTrace}");

        if (_pendingChunksByWindow.IsEmpty)
        {
            Console.WriteLine("🔥 [PHASE12.2_INTERNAL] _pendingChunksByWindow is empty - 早期リターン");
            _logger?.LogDebug("🔥 [PHASE12.2_INTERNAL] _pendingChunksByWindow is empty - 早期リターン");
            return;
        }

        // 1. 処理対象をアトミックに取得・削除（データロスト防止）
        var chunksToProcessByWindow = new Dictionary<IntPtr, List<TextChunk>>();
        var windowHandles = _pendingChunksByWindow.Keys.ToList();
        foreach (var handle in windowHandles)
        {
            if (_pendingChunksByWindow.TryRemove(handle, out var chunks))
            {
                chunksToProcessByWindow[handle] = chunks;
            }
        }
        
        // 🚨 [CRITICAL_DEBUG] Line 496診断
        try { Console.WriteLine("🚨 [LINE496_BEFORE] Sum計算直前"); } catch { }
        var totalInputChunks = chunksToProcessByWindow.Values.Sum(list => list.Count);
        try { Console.WriteLine($"🚨 [LINE496_AFTER] Sum計算完了 - totalInputChunks: {totalInputChunks}"); } catch { }
        try { _logger?.LogDebug($"🚨 [LINE496_AFTER] Sum計算完了 - totalInputChunks: {totalInputChunks}"); } catch { }

        try { Console.WriteLine($"🚨🚨🚨 [COMBINE_DEBUG] 統合処理開始 - {chunksToProcessByWindow.Count}ウィンドウ, {totalInputChunks}個のチャンク"); } catch { }
        try { _logger?.LogDebug($"🚨🚨🚨 [COMBINE_DEBUG] 統合処理開始 - {chunksToProcessByWindow.Count}ウィンドウ, {totalInputChunks}個のチャンク"); } catch { }
        _logger.LogCritical("🚨🚨🚨 [COMBINE_DEBUG] 統合処理開始 - {WindowCount}ウィンドウ, {Count}個のチャンク",
            chunksToProcessByWindow.Count, totalInputChunks);

        try
        {
            var allAggregatedChunks = new List<TextChunk>();

            // ウィンドウハンドル別に統合処理（コンテキスト分離）
            foreach (var kvp in chunksToProcessByWindow)
            {
                var windowHandle = kvp.Key;
                var chunksForWindow = kvp.Value;

                var windowLog = $"🚨 [COMBINE_DEBUG] ウィンドウ {windowHandle}: {chunksForWindow.Count}個のチャンク処理開始";
                Console.WriteLine(windowLog);
                _logger?.LogDebug(windowLog);
                _logger.LogCritical(windowLog);

                if (chunksForWindow.Count > 0)
                {
                    // 🚨 [COMBINE_DEBUG] 入力チャンクの詳細情報をログ出力
                    var logMsg = $"🚨 [COMBINE_DEBUG] CombineChunks呼び出し直前 - Count: {chunksForWindow.Count}";
                    Console.WriteLine(logMsg);
                    _logger?.LogDebug(logMsg);
                    _logger.LogCritical(logMsg);

                    var maxLog = Math.Min(chunksForWindow.Count, 5); // 最初の5個だけログ出力
                    for (int i = 0; i < maxLog; i++)
                    {
                        var chunk = chunksForWindow[i];
                        var chunkLog = $"  🔍 [INPUT_CHUNK_{i}] ID:{chunk.ChunkId}, Text:「{chunk.CombinedText}」, Bounds:(X:{chunk.CombinedBounds.X}, Y:{chunk.CombinedBounds.Y}, W:{chunk.CombinedBounds.Width}, H:{chunk.CombinedBounds.Height})";
                        Console.WriteLine(chunkLog);
                        _logger?.LogDebug(chunkLog);
                    }
                    if (chunksForWindow.Count > 5)
                    {
                        var omitLog = $"  ... (残り {chunksForWindow.Count - 5} 個のチャンクは省略)";
                        Console.WriteLine(omitLog);
                        _logger?.LogDebug(omitLog);
                    }

                    var aggregatedChunks = CombineChunks(chunksForWindow);

                    Console.WriteLine($"🚨 [COMBINE_DEBUG] CombineChunks呼び出し完了 - 結果: {aggregatedChunks.Count}個");
                    for (int i = 0; i < aggregatedChunks.Count; i++)
                    {
                        var chunk = aggregatedChunks[i];
                        Console.WriteLine($"  ✅ [OUTPUT_CHUNK_{i}] ID:{chunk.ChunkId}, Text:「{chunk.CombinedText}」, Bounds:(X:{chunk.CombinedBounds.X}, Y:{chunk.CombinedBounds.Y}, W:{chunk.CombinedBounds.Width}, H:{chunk.CombinedBounds.Height})");
                    }
                    allAggregatedChunks.AddRange(aggregatedChunks);
                    
                    _logger.LogDebug("ウィンドウ {WindowHandle}: {InputCount}個→{OutputCount}個のチャンク統合",
                        windowHandle, chunksForWindow.Count, aggregatedChunks.Count);
                }
            }
            
            // 統合されたチャンクを翻訳パイプラインに送信
            if (allAggregatedChunks.Count > 0)
            {
                // 🎯 Phase 12.2: AggregatedChunksReadyEventを発行（イベント駆動アーキテクチャ）
                var windowHandle = allAggregatedChunks.FirstOrDefault()?.SourceWindowHandle ?? IntPtr.Zero;
                var aggregatedEvent = new AggregatedChunksReadyEvent(
                    allAggregatedChunks.AsReadOnly(),
                    windowHandle
                );

                // 🔥 [PHASE12.2_INTERNAL] イベント発行直前
                Console.WriteLine($"🔥 [PHASE12.2_INTERNAL] AggregatedChunksReadyEvent発行直前 - SessionId: {aggregatedEvent.SessionId}, ChunkCount: {allAggregatedChunks.Count}");
                _logger?.LogDebug($"🔥 [PHASE12.2_INTERNAL] AggregatedChunksReadyEvent発行直前 - SessionId: {aggregatedEvent.SessionId}, ChunkCount: {allAggregatedChunks.Count}");

                await _eventAggregator.PublishAsync(aggregatedEvent).ConfigureAwait(false);

                // 🔥 [PHASE12.2_INTERNAL] イベント発行完了
                Console.WriteLine($"🔥🔥🔥 [PHASE12.2_INTERNAL] AggregatedChunksReadyEvent発行完了 - SessionId: {aggregatedEvent.SessionId}, ChunkCount: {allAggregatedChunks.Count}");
                _logger?.LogDebug($"🔥🔥🔥 [PHASE12.2_INTERNAL] AggregatedChunksReadyEvent発行完了 - SessionId: {aggregatedEvent.SessionId}, ChunkCount: {allAggregatedChunks.Count}");

                _logger.LogInformation("🎯 [PHASE12.2] AggregatedChunksReadyEvent発行完了 - SessionId: {SessionId}, ChunkCount: {Count}",
                    aggregatedEvent.SessionId, allAggregatedChunks.Count);
            }

            Interlocked.Increment(ref _totalAggregationEvents);
            
            _logger.LogInformation("🎯 統合処理完了 - {InputCount}個→{OutputCount}個のチャンク", 
                totalInputChunks, allAggregatedChunks.Count);
                
            // パフォーマンス統計ログ
            if (_settings.CurrentValue.EnablePerformanceLogging && _totalAggregationEvents % 10 == 0)
            {
                LogPerformanceStatistics();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "チャンク統合処理中にエラーが発生");
            
            // データロスト防止: エラー時は処理失敗したチャンクをキューに戻す
            foreach (var kvp in chunksToProcessByWindow)
            {
                var windowHandle = kvp.Key;
                var failedChunks = kvp.Value;
                
                // 既存のエントリがあれば先頭に挿入、なければ新規作成
                _pendingChunksByWindow.AddOrUpdate(windowHandle, 
                    failedChunks, 
                    (key, existingChunks) => 
                    {
                        failedChunks.AddRange(existingChunks);
                        return failedChunks;
                    });
                    
                _logger.LogWarning("エラー時データ復旧 - ウィンドウ {WindowHandle}: {Count}個のチャンクをキューに復元", 
                    windowHandle, failedChunks.Count);
            }
            
            throw;
        }
    }

    /// <summary>
    /// 複数チャンクを近接度ベースでグループ化・統合
    /// UltraThink Phase 1: 自動適応アルゴリズム実装
    /// </summary>
    private List<TextChunk> CombineChunks(List<TextChunk> chunks)
    {
        if (chunks.Count == 0) return [];
        if (chunks.Count == 1) return chunks;

        try
        {
            // 🚨 [ULTRA_DEBUG] CombineChunksメソッド実行確認
            Console.WriteLine($"🚨🚨🚨 [ULTRA_DEBUG] CombineChunksメソッド実行開始！ - Count: {chunks.Count}");
            _logger?.LogDebug($"🚨🚨🚨 [ULTRA_DEBUG] CombineChunksメソッド実行開始！ - Count: {chunks.Count}");
            _logger.LogCritical("🚨🚨🚨 [ULTRA_DEBUG] CombineChunksメソッド実行開始！ - Count: {Count}", chunks.Count);

            // 🔍 [DEBUG] 設定値のログ出力
            var enabled = _settings.CurrentValue.ProximityGrouping.Enabled;
            var verticalFactor = _settings.CurrentValue.ProximityGrouping.VerticalDistanceFactor;
            Console.WriteLine($"🚨🚨🚨 [SETTINGS_DEBUG] ProximityGrouping.Enabled: {enabled}, VerticalDistanceFactor: {verticalFactor}");
            _logger?.LogDebug($"🚨🚨🚨 [SETTINGS_DEBUG] ProximityGrouping.Enabled: {enabled}, VerticalDistanceFactor: {verticalFactor}");
            _logger.LogCritical("🚨🚨🚨 [SETTINGS_DEBUG] ProximityGrouping.Enabled: {Enabled}, VerticalDistanceFactor: {Factor}",
                enabled, verticalFactor);

            // 近接度グループ化が無効の場合は従来通りの統合
            if (!enabled)
            {
                _logger.LogInformation("🔄 [LEGACY] 近接度グループ化無効 - 従来の統合処理実行: {Count}個", chunks.Count);
                return LegacyCombineChunks(chunks);
            }

            // 🎯 UltraThink Phase 1: 近接度ベースグループ化実行
            _logger.LogInformation("🔍 [ULTRATHINK] 近接度ベースグループ化開始 - 入力: {Count}個", chunks.Count);

            // 近接度でグループ化
            var proximityGroups = _proximityGroupingService.GroupByProximity(chunks);

            if (proximityGroups.Count == 0)
            {
                _logger.LogWarning("⚠️ [ULTRATHINK] グループ化結果が空 - 元のチャンクを返します");
                return chunks;
            }

            // 各グループを個別に統合
            var combinedChunks = new List<TextChunk>();
            for (int groupIndex = 0; groupIndex < proximityGroups.Count; groupIndex++)
            {
                var group = proximityGroups[groupIndex];
                var combinedChunk = CombineSingleGroup(group, groupIndex);
                combinedChunks.Add(combinedChunk);
            }

            _logger.LogInformation("✅ [ULTRATHINK] 近接度グループ化完了 - " +
                "入力: {InputCount}個 → 出力: {OutputCount}個 " +
                "({GroupCount}グループ)",
                chunks.Count, combinedChunks.Count, proximityGroups.Count);

            return combinedChunks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 [ULTRATHINK] 近接度グループ化でエラー - レガシー処理にフォールバック");
            // エラー時はレガシー処理にフォールバック
            return LegacyCombineChunks(chunks);
        }
    }

    /// <summary>
    /// 単一グループを統合（従来のロジック適用）
    /// </summary>
    private TextChunk CombineSingleGroup(List<TextChunk> groupChunks, int groupIndex)
    {
        if (groupChunks.Count == 1)
        {
            _logger.LogDebug("📦 [GROUP_{Index}] 単一チャンク - 統合不要: {ChunkId}",
                groupIndex, groupChunks[0].ChunkId);
            return groupChunks[0];
        }

        // 従来の座標ベース改行処理を適用
        var combinedText = _lineBreakProcessor.ProcessLineBreaks(groupChunks);
        var combinedBounds = CalculateCombinedBounds(groupChunks);

        var combinedChunk = new TextChunk
        {
            ChunkId = GenerateNewChunkId(),
            TextResults = groupChunks.SelectMany(c => c.TextResults).ToList(),
            CombinedBounds = combinedBounds,
            CombinedText = combinedText,
            SourceWindowHandle = groupChunks[0].SourceWindowHandle,
            DetectedLanguage = groupChunks[0].DetectedLanguage
        };

        if (_settings.CurrentValue.ProximityGrouping.EnableDetailedLogging)
        {
            var chunkIds = string.Join(", ", groupChunks.Select(c => c.ChunkId));
            _logger.LogDebug("📦 [GROUP_{Index}] 統合完了 - " +
                "入力: [{ChunkIds}] → 出力: {OutputId}, " +
                "テキスト: 「{Text}」",
                groupIndex, chunkIds, combinedChunk.ChunkId,
                combinedText.Length > 50 ? combinedText[..50] + "..." : combinedText);
        }

        return combinedChunk;
    }

    /// <summary>
    /// レガシー統合処理（近接度グループ化無効時のフォールバック）
    /// </summary>
    private List<TextChunk> LegacyCombineChunks(List<TextChunk> chunks)
    {
        // 従来の無条件統合処理
        var combinedText = _lineBreakProcessor.ProcessLineBreaks(chunks);
        var combinedBounds = CalculateCombinedBounds(chunks);

        var combinedChunk = new TextChunk
        {
            ChunkId = GenerateNewChunkId(),
            TextResults = chunks.SelectMany(c => c.TextResults).ToList(),
            CombinedBounds = combinedBounds,
            CombinedText = combinedText,
            SourceWindowHandle = chunks[0].SourceWindowHandle,
            DetectedLanguage = chunks[0].DetectedLanguage
        };

        _logger.LogDebug("🔄 [LEGACY] 従来統合完了 - {Count}個 → 1個: 「{Text}」",
            chunks.Count, combinedText.Length > 50 ? combinedText[..50] + "..." : combinedText);

        return [combinedChunk];
    }

    /// <summary>
    /// 統合されたバウンディングボックスを計算
    /// UltraThink P0: ROI座標からスクリーン座標への適切な変換を実装
    /// </summary>
    private System.Drawing.Rectangle CalculateCombinedBounds(List<TextChunk> chunks)
    {
        if (chunks.Count == 0) return System.Drawing.Rectangle.Empty;

        if (chunks.Count == 1)
        {
            var singleChunk = chunks[0];
            // 🎯 [P0_COORDINATE_TRANSFORM] 単一チャンクのROI→スクリーン座標変換
            return _coordinateTransformationService.ConvertRoiToScreenCoordinates(
                singleChunk.CombinedBounds, singleChunk.SourceWindowHandle);
        }

        // 🎯 [P0_COORDINATE_TRANSFORM] 複数チャンクの一括座標変換
        var windowHandle = chunks[0].SourceWindowHandle;
        var roiBounds = chunks.Select(c => c.CombinedBounds).ToArray();
        var screenBounds = _coordinateTransformationService.ConvertRoiToScreenCoordinatesBatch(
            roiBounds, windowHandle);

        // 変換された座標から統合バウンディングボックスを計算
        var minX = screenBounds.Min(r => r.X);
        var minY = screenBounds.Min(r => r.Y);
        var maxRight = screenBounds.Max(r => r.Right);
        var maxBottom = screenBounds.Max(r => r.Bottom);

        var combinedBounds = new System.Drawing.Rectangle(minX, minY, maxRight - minX, maxBottom - minY);

        _logger.LogDebug("🎯 [P0_COORDINATE_TRANSFORM] 統合バウンディングボックス計算完了: ChunkCount={Count}, ROI→Screen変換済み, Result=({X},{Y},{W},{H})",
            chunks.Count, combinedBounds.X, combinedBounds.Y, combinedBounds.Width, combinedBounds.Height);

        return combinedBounds;
    }

    /// <summary>
    /// 新しいChunkIDを生成
    /// 戦略書フィードバック反映: スレッドセーフなID生成
    /// </summary>
    private int GenerateNewChunkId()
    {
        return Interlocked.Increment(ref _nextChunkId);
    }
    
    /// <summary>
    /// パフォーマンス統計をログ出力
    /// </summary>
    private void LogPerformanceStatistics()
    {
        var totalProcessedChunks = Interlocked.Read(ref _totalChunksProcessed);
        var totalEvents = Interlocked.Read(ref _totalAggregationEvents);
        var averageChunksPerEvent = totalEvents > 0 ? totalProcessedChunks / (double)totalEvents : 0;
        
        _logger.LogInformation("📊 TimedChunkAggregator統計 - 処理チャンク: {Total}, 集約イベント: {Events}, 平均: {Avg:F1}チャンク/イベント",
            totalProcessedChunks, totalEvents, averageChunksPerEvent);
    }

    /// <summary>
    /// 現在の統計情報を取得
    /// </summary>
    public (long TotalChunksProcessed, long TotalAggregationEvents) GetStatistics()
    {
        return (Interlocked.Read(ref _totalChunksProcessed), Interlocked.Read(ref _totalAggregationEvents));
    }

    public void Dispose()
    {
        _aggregationTimer?.Dispose();
        _processingLock?.Dispose();
        _settingsChangeSubscription?.Dispose();
        
        if (_settings.CurrentValue.EnablePerformanceLogging)
        {
            LogPerformanceStatistics();
        }
        
        _logger.LogInformation("🧹 TimedChunkAggregator disposed - 最終統計: {Chunks}チャンク, {Events}イベント", 
            _totalChunksProcessed, _totalAggregationEvents);
    }
}