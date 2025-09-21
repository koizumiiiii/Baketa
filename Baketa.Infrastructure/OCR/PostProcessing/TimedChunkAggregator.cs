using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Settings;

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
    
    // 設定可能なバッファ時間
    private readonly TimedAggregatorSettings _settings;
    
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
        ILogger<TimedChunkAggregator> logger)
    {
        // 引数バリデーション（logger を最初に設定）
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lineBreakProcessor = lineBreakProcessor ?? throw new ArgumentNullException(nameof(lineBreakProcessor));
        _coordinateTransformationService = coordinateTransformationService ?? throw new ArgumentNullException(nameof(coordinateTransformationService));
        
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
            }
        }
        
        _settings = settings?.CurrentValue ?? TimedAggregatorSettings.Development;
        
        // フォールバック後の設定値も確認
        _logger.LogDebug("🔍 [CONFIG_DEBUG] Final _settings.IsFeatureEnabled: {Enabled}", _settings.IsFeatureEnabled);
        _logger.LogDebug("🔍 [CONFIG_DEBUG] Final _settings.BufferDelayMs: {DelayMs}", _settings.BufferDelayMs);
        _logger.LogDebug("🔍 [CONFIG_DEBUG] TimedAggregatorSettings.Development.IsFeatureEnabled: {DevEnabled}", TimedAggregatorSettings.Development.IsFeatureEnabled);
        
        _pendingChunksByWindow = new ConcurrentDictionary<IntPtr, List<TextChunk>>();
        _processingLock = new SemaphoreSlim(1, 1);
        _performanceStopwatch = new System.Diagnostics.Stopwatch();
        _lastTimerReset = DateTime.UtcNow;
        _nextChunkId = Random.Shared.Next(1000000, 9999999);
        
        _aggregationTimer = new System.Threading.Timer(ProcessPendingChunks, null, 
            Timeout.Infinite, Timeout.Infinite);
            
        _logger.LogInformation("🧩 TimedChunkAggregator初期化完了 - BufferDelay: {DelayMs}ms, Feature: {Enabled}", 
            _settings.BufferDelayMs, _settings.IsFeatureEnabled);
    }

    /// <summary>
    /// 新しいチャンクを追加し、タイマーをリセット
    /// 戦略書フィードバック反映: SourceWindowHandle別管理、ForceFlushMs制御
    /// </summary>
    public async Task<bool> TryAddChunkAsync(TextChunk chunk, CancellationToken cancellationToken = default)
    {
        // Feature Flag チェック - 機能が無効の場合は即座にfalseを返す
        if (!_settings.IsFeatureEnabled)
        {
            _logger.LogDebug("TimedChunkAggregator機能が無効化されています");
            return false;
        }

        _logger.LogDebug("🔐 [PHASE_C_DEBUG] TryAddChunkAsync開始 - ロック取得試行中");
        await _processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
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

            // 全ウィンドウのチャンク数を計算
            var totalChunks = _pendingChunksByWindow.Values.Sum(list => list.Count);

            // メモリ保護：最大チャンク数を超えたら強制処理
            if (totalChunks >= _settings.MaxChunkCount)
            {
                _logger.LogWarning("⚠️ [Phase20] 最大チャンク数到達 - 強制処理開始: {Count}個 (設定値: {MaxCount})",
                    totalChunks, _settings.MaxChunkCount);
                await ProcessPendingChunksInternal().ConfigureAwait(false);
                return true;
            }

            // ForceFlushMs制御: 無限タイマーリセットを防ぐ
            var timeSinceLastReset = DateTime.UtcNow - _lastTimerReset;
            if (timeSinceLastReset.TotalMilliseconds >= _settings.ForceFlushMs)
            {
                _logger.LogWarning("🚨 [PHASE_20_EMERGENCY] ForceFlushMs到達 - タイマー長期停止検出: {ElapsedMs}ms経過 (設定値: {ForceFlushMs}ms)",
                    timeSinceLastReset.TotalMilliseconds, _settings.ForceFlushMs);

                // 🚀 Phase 20緊急修正: ForceFlushMs後にタイマーを強制リセット
                try
                {
                    await ProcessPendingChunksInternal().ConfigureAwait(false);

                    // タイマーを強制的に再起動（Phase 20追加）
                    bool emergencyTimerReset = _aggregationTimer.Change(_settings.BufferDelayMs, Timeout.Infinite);
                    _lastTimerReset = DateTime.UtcNow;

                    _logger.LogInformation("🔧 [PHASE_20_EMERGENCY] 緊急タイマーリセット実行 - 結果: {Result}, {DelayMs}ms後に再開予定",
                        emergencyTimerReset, _settings.BufferDelayMs);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🚨 [PHASE_20_EMERGENCY] 緊急タイマーリセット失敗");
                }
            }
            else
            {
                // 🚀 Phase 19緊急修正: タイマー確実実行保証とタイマー状況監視
                try
                {
                    var timerResetStart = DateTime.UtcNow;
                    _logger.LogDebug("🔄 [PHASE_19_FIX] タイマーリセット開始 - DelayMs: {DelayMs}, Current: {CurrentTime}",
                        _settings.BufferDelayMs, timerResetStart);

                    // タイマーをリセット（新しいチャンクが来たら待ち時間をリセット）
                    bool timerChangeResult = _aggregationTimer.Change(_settings.BufferDelayMs, Timeout.Infinite);
                    _lastTimerReset = DateTime.UtcNow; // タイマーリセット時刻を記録

                    _logger.LogInformation("⏱️ [PHASE_19_FIX] タイマーリセット完了 - 結果: {Result}, {DelayMs}ms後に処理予定 (バッファ中: {Count}個)",
                        timerChangeResult, _settings.BufferDelayMs, totalChunks);

                    // タイマー実行監視用のバックアップタスク（Phase 19安全機構）
                    var expectedFireTime = DateTime.UtcNow.AddMilliseconds(_settings.BufferDelayMs + 50); // 50ms余裕
                    _ = Task.Delay(_settings.BufferDelayMs + 100).ContinueWith(async _ =>
                    {
                        try
                        {
                            var now = DateTime.UtcNow;
                            var timeSinceReset = (now - _lastTimerReset).TotalMilliseconds;

                            if (timeSinceReset >= _settings.BufferDelayMs + 50 && _pendingChunksByWindow.Count > 0)
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
        // 🚀 Phase 19強化: タイマーコールバック実行状況詳細監視
        var callbackStart = DateTime.UtcNow;
        var timeSinceLastReset = (callbackStart - _lastTimerReset).TotalMilliseconds;

        _logger.LogInformation("🔥 [PHASE_19_CALLBACK] タイマーコールバック実行開始 - リセットから{ElapsedMs}ms経過, 期待値: {ExpectedMs}ms",
            timeSinceLastReset, _settings.BufferDelayMs);

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

                    // OnChunksAggregatedコールバックを実行
                    if (OnChunksAggregated != null)
                    {
                        await OnChunksAggregated.Invoke(new List<TextChunk> { fallbackChunk }).ConfigureAwait(false);
                        _logger.LogInformation("✅ [PHASE_A_FIX] フォールバック処理成功 - OnChunksAggregated実行完了 (テキスト長: {Length})",
                            combinedText.Length);

                        // 統計更新
                        Interlocked.Increment(ref _totalAggregationEvents);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [PHASE_A_FIX] OnChunksAggregatedがnull - コールバック実行不可");
                    }
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
        if (_pendingChunksByWindow.IsEmpty) return;

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
        
        var totalInputChunks = chunksToProcessByWindow.Values.Sum(list => list.Count);
        _logger.LogDebug("統合処理開始 - {WindowCount}ウィンドウ, {Count}個のチャンク", 
            chunksToProcessByWindow.Count, totalInputChunks);

        try
        {
            var allAggregatedChunks = new List<TextChunk>();
            
            // ウィンドウハンドル別に統合処理（コンテキスト分離）
            foreach (var kvp in chunksToProcessByWindow)
            {
                var windowHandle = kvp.Key;
                var chunksForWindow = kvp.Value;
                
                if (chunksForWindow.Count > 0)
                {
                    var aggregatedChunks = CombineChunks(chunksForWindow);
                    allAggregatedChunks.AddRange(aggregatedChunks);
                    
                    _logger.LogDebug("ウィンドウ {WindowHandle}: {InputCount}個→{OutputCount}個のチャンク統合",
                        windowHandle, chunksForWindow.Count, aggregatedChunks.Count);
                }
            }
            
            // 統合されたチャンクを翻訳パイプラインに送信
            if (OnChunksAggregated != null && allAggregatedChunks.Count > 0)
            {
                await OnChunksAggregated.Invoke(allAggregatedChunks).ConfigureAwait(false);
            }
            
            Interlocked.Increment(ref _totalAggregationEvents);
            
            _logger.LogInformation("🎯 統合処理完了 - {InputCount}個→{OutputCount}個のチャンク", 
                totalInputChunks, allAggregatedChunks.Count);
                
            // パフォーマンス統計ログ
            if (_settings.EnablePerformanceLogging && _totalAggregationEvents % 10 == 0)
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
    /// 複数チャンクを統合（既存のCoordinateBasedLineBreakProcessorを活用）
    /// </summary>
    private List<TextChunk> CombineChunks(List<TextChunk> chunks)
    {
        if (chunks.Count == 0) return [];
        if (chunks.Count == 1) return chunks;

        try
        {
            // 🔍 Phase 20: 結合前のテキストをログ出力
            _logger.LogInformation("🔍 [Phase20] チャンク結合前 - {Count}個のチャンク:", chunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var chunkText = chunk.CombinedText ?? chunk.TextResults?.FirstOrDefault()?.Text ?? "";
                _logger.LogInformation("  [Chunk {Index}] ID:{ChunkId}, Bounds:({X},{Y},{W},{H}), Text:「{Text}」",
                    i, chunk.ChunkId,
                    chunk.CombinedBounds.X, chunk.CombinedBounds.Y,
                    chunk.CombinedBounds.Width, chunk.CombinedBounds.Height,
                    chunkText);
            }

            // 座標ベースでグループ化・統合
            var combinedText = _lineBreakProcessor.ProcessLineBreaks(chunks);

            // 統合されたテキストから新しいTextChunkを作成
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

            // 🎯 Phase 20: 結合後のテキストをログ出力
            _logger.LogInformation("🎯 [Phase20] チャンク結合後:");
            _logger.LogInformation("  新ChunkID:{ChunkId}, Bounds:({X},{Y},{W},{H})",
                combinedChunk.ChunkId,
                combinedBounds.X, combinedBounds.Y,
                combinedBounds.Width, combinedBounds.Height);
            _logger.LogInformation("  結合後テキスト:「{Text}」", combinedText);
            _logger.LogInformation("  文字数: {Length}文字, 改行数: {LineCount}",
                combinedText.Length,
                combinedText.Count(c => c == '\n'));

            return [combinedChunk];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "チャンク統合中にエラー - フォールバック処理実行");
            // エラー時は元のチャンクをそのまま返す（フォールバック）
            return chunks;
        }
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
    /// 集約完了イベント
    /// </summary>
    public Func<List<TextChunk>, Task>? OnChunksAggregated { get; set; }
    
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
        
        if (_settings.EnablePerformanceLogging)
        {
            LogPerformanceStatistics();
        }
        
        _logger.LogInformation("🧹 TimedChunkAggregator disposed - 最終統計: {Chunks}チャンク, {Events}イベント", 
            _totalChunksProcessed, _totalAggregationEvents);
    }
}