using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.Translation;
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
        ILogger<TimedChunkAggregator> logger)
    {
        // 引数バリデーション（logger を最初に設定）
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lineBreakProcessor = lineBreakProcessor ?? throw new ArgumentNullException(nameof(lineBreakProcessor));
        
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

        await _processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // パフォーマンス計測開始
            _performanceStopwatch.Start();
            
            // SourceWindowHandle別にバッファを分離（コンテキスト混在防止）
            var windowHandle = chunk.SourceWindowHandle;
            if (!_pendingChunksByWindow.ContainsKey(windowHandle))
            {
                _pendingChunksByWindow[windowHandle] = new List<TextChunk>();
            }
            
            _pendingChunksByWindow[windowHandle].Add(chunk);
            Interlocked.Increment(ref _totalChunksProcessed);
            
            // 全ウィンドウのチャンク数を計算
            var totalChunks = _pendingChunksByWindow.Values.Sum(list => list.Count);
            
            // メモリ保護：最大チャンク数を超えたら強制処理
            if (totalChunks >= _settings.MaxChunkCount)
            {
                _logger.LogWarning("最大チャンク数到達 - 強制処理開始: {Count}個", totalChunks);
                await ProcessPendingChunksInternal().ConfigureAwait(false);
                return true;
            }
            
            // ForceFlushMs制御: 無限タイマーリセットを防ぐ
            var timeSinceLastReset = DateTime.UtcNow - _lastTimerReset;
            if (timeSinceLastReset.TotalMilliseconds >= _settings.ForceFlushMs)
            {
                _logger.LogDebug("ForceFlushMs到達 - 強制処理実行: {ElapsedMs}ms経過", timeSinceLastReset.TotalMilliseconds);
                await ProcessPendingChunksInternal().ConfigureAwait(false);
            }
            else
            {
                // タイマーをリセット（新しいチャンクが来たら待ち時間をリセット）
                _aggregationTimer.Change(_settings.BufferDelayMs, Timeout.Infinite);
                _lastTimerReset = DateTime.UtcNow; // タイマーリセット時刻を記録
            }
            
            _logger.LogDebug("チャンク追加 - ウィンドウ: {WindowHandle}, 合計: {Count}個, 次回処理: {DelayMs}ms後", 
                windowHandle, totalChunks, _settings.BufferDelayMs);
            
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
    /// Gemini指摘反映: async void避けのため同期メソッドでラップ
    /// </summary>
    private void ProcessPendingChunks(object? state)
    {
        // Fire-and-forgetパターンで非同期処理を実行
        _ = ProcessPendingChunksAsync();
    }

    /// <summary>
    /// バッファされたチャンクを統合処理（非同期実装）
    /// Gemini指摘反映: async void回避とタイムアウト追加によるデッドロック防止
    /// </summary>
    private async Task ProcessPendingChunksAsync()
    {
        // タイムアウト付きロック取得でデッドロック防止
        if (!await _processingLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
        {
            _logger.LogWarning("⚠️ ProcessPendingChunksAsyncのロック取得がタイムアウトしました。");
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
        if (chunks.Count == 0) return new List<TextChunk>();
        if (chunks.Count == 1) return chunks;

        try
        {
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

            _logger.LogTrace("チャンク統合完了: {InputCount}個 → 1個, テキスト: '{Text}'", 
                chunks.Count, combinedText.Length > 50 ? combinedText[..50] + "..." : combinedText);

            return new List<TextChunk> { combinedChunk };
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
    /// </summary>
    private System.Drawing.Rectangle CalculateCombinedBounds(List<TextChunk> chunks)
    {
        if (chunks.Count == 0) return System.Drawing.Rectangle.Empty;
        if (chunks.Count == 1) return chunks[0].CombinedBounds;

        var minX = chunks.Min(c => c.CombinedBounds.X);
        var minY = chunks.Min(c => c.CombinedBounds.Y);
        var maxRight = chunks.Max(c => c.CombinedBounds.Right);
        var maxBottom = chunks.Max(c => c.CombinedBounds.Bottom);

        return new System.Drawing.Rectangle(minX, minY, maxRight - minX, maxBottom - minY);
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