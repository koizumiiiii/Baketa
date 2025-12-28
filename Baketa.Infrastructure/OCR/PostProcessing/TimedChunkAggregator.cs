using System.Collections.Concurrent;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events.Translation;
using Baketa.Core.Settings;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// 時間軸ベースのTextChunk集約処理クラス
/// OCR結果を一定時間バッファリングし、統合してから翻訳パイプラインに送信
/// 戦略書設計: translation-quality-improvement-strategy.md 完全準拠実装
/// PP-OCRv5削除後: ITextChunkAggregatorService実装を追加
/// </summary>
public sealed class TimedChunkAggregator : ITextChunkAggregatorService, IDisposable
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

    // [Issue #78 Phase 4] Cloud AI翻訳用画像コンテキスト
    private string? _currentImageBase64;
    private int _currentImageWidth;
    private int _currentImageHeight;
    private readonly object _imageContextLock = new();

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
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // 設定変更の動的反映を購読
        _settingsChangeSubscription = _settings.OnChange((newSettings, _) =>
        {
            _logger.LogInformation("設定変更を検出: IsFeatureEnabled={Enabled}, BufferDelayMs={DelayMs}",
                newSettings.IsFeatureEnabled, newSettings.BufferDelayMs);
        });

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
    /// 現在の集約待機チャンク数を取得します
    /// ITextChunkAggregatorService実装
    /// </summary>
    public int PendingChunksCount => _pendingChunksByWindow.Values.Sum(list => list.Count);

    /// <summary>
    /// ITextChunkAggregatorService.TryAddTextChunkAsync実装
    /// 内部のTryAddChunkAsyncに委譲
    /// </summary>
    public Task<bool> TryAddTextChunkAsync(TextChunk chunk, CancellationToken cancellationToken = default)
        => TryAddChunkAsync(chunk, cancellationToken);

    /// <summary>
    /// [Issue #227] ITextChunkAggregatorService.TryAddTextChunksBatchAsync実装
    /// 複数チャンクを1回のロックで追加（N+1ロック問題解消）
    /// </summary>
    private const int MaxBatchSize = 500;

    public async Task<int> TryAddTextChunksBatchAsync(IReadOnlyList<TextChunk> chunks, CancellationToken cancellationToken = default)
    {
        if (chunks == null || chunks.Count == 0)
            return 0;

        // Feature Flag チェック
        if (!_settings.CurrentValue.IsFeatureEnabled)
        {
            _logger.LogDebug("TimedChunkAggregator機能が無効化されています");
            return 0;
        }

        // [Gemini Review] バッチサイズ制限 - メモリ枯渇防止
        if (chunks.Count > MaxBatchSize)
        {
            _logger.LogWarning("バッチサイズ{Count}が上限{Max}を超えています - 分割処理",
                chunks.Count, MaxBatchSize);

            var totalAdded = 0;
            for (var i = 0; i < chunks.Count; i += MaxBatchSize)
            {
                var batch = chunks.Skip(i).Take(MaxBatchSize).ToList();
                totalAdded += await TryAddTextChunksBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            return totalAdded;
        }

        await _processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var addedCount = 0;

            foreach (var chunk in chunks)
            {
                var windowHandle = chunk.SourceWindowHandle;
                if (!_pendingChunksByWindow.TryGetValue(windowHandle, out var existingChunks))
                {
                    existingChunks = [];
                    _pendingChunksByWindow[windowHandle] = existingChunks;
                }

                // 最初のチャンク追加時にタイマーリセット時刻を更新
                if (existingChunks.Count == 0)
                {
                    _lastTimerReset = DateTime.UtcNow;
                }

                existingChunks.Add(chunk);
                addedCount++;
                Interlocked.Increment(ref _totalChunksProcessed);
            }

            var totalChunks = _pendingChunksByWindow.Values.Sum(list => list.Count);

            // メモリ保護：最大チャンク数を超えたら強制処理
            if (totalChunks >= _settings.CurrentValue.MaxChunkCount)
            {
                _logger.LogWarning("最大チャンク数到達 - 強制処理: {Count}個", totalChunks);
                await ProcessPendingChunksInternal().ConfigureAwait(false);
                return addedCount;
            }

            // タイマーリセット（1回のみ）
            _aggregationTimer.Change(_settings.CurrentValue.BufferDelayMs, Timeout.Infinite);
            _lastTimerReset = DateTime.UtcNow;

            _logger.LogDebug("バッチ追加完了: {AddedCount}個追加, 合計{TotalCount}個待機中",
                addedCount, totalChunks);

            return addedCount;
        }
        finally
        {
            _processingLock.Release();
        }
    }

    /// <summary>
    /// 新しいチャンクを追加し、タイマーをリセット
    /// 戦略書フィードバック反映: SourceWindowHandle別管理、ForceFlushMs制御
    /// </summary>
    public async Task<bool> TryAddChunkAsync(TextChunk chunk, CancellationToken cancellationToken = default)
    {
        // [Issue #227] デバッグログ削除 - パフォーマンス改善

        // Feature Flag チェック - 機能が無効の場合は即座にfalseを返す
        if (!_settings.CurrentValue.IsFeatureEnabled)
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

            // メモリ保護：最大チャンク数を超えたら強制処理
            if (totalChunks >= _settings.CurrentValue.MaxChunkCount)
            {
                _logger.LogWarning("最大チャンク数到達 - 強制処理: {Count}個", totalChunks);
                await ProcessPendingChunksInternal().ConfigureAwait(false);
                return true;
            }

            // ForceFlushMs制御: 無限タイマーリセットを防ぐ
            var timeSinceLastReset = DateTime.UtcNow - _lastTimerReset;

            if (timeSinceLastReset.TotalMilliseconds >= _settings.CurrentValue.ForceFlushMs)
            {
                _logger.LogWarning("ForceFlushMs到達 - 強制処理: {ElapsedMs}ms経過", timeSinceLastReset.TotalMilliseconds);

                try
                {
                    await ProcessPendingChunksInternal().ConfigureAwait(false);
                    _aggregationTimer.Change(_settings.CurrentValue.BufferDelayMs, Timeout.Infinite);
                    _lastTimerReset = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "緊急タイマーリセット失敗");
                }
            }
            else
            {
                // 通常タイマーリセット
                try
                {
                    _aggregationTimer.Change(_settings.CurrentValue.BufferDelayMs, Timeout.Infinite);
                    _lastTimerReset = DateTime.UtcNow;

                    _logger.LogDebug("タイマーリセット: {DelayMs}ms後に処理予定 (バッファ: {Count}個)",
                        _settings.CurrentValue.BufferDelayMs, totalChunks);

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
    /// </summary>
    private async void ProcessPendingChunks(object? state)
    {
        var callbackStart = DateTime.UtcNow;

        try
        {
            await ProcessPendingChunksInternal().ConfigureAwait(false);
            _logger.LogDebug("タイマーコールバック完了: {ProcessingMs}ms", (DateTime.UtcNow - callbackStart).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "タイマーコールバック実行失敗 - フォールバック処理実行");

            try
            {
                await ExecuteFallbackProcessing().ConfigureAwait(false);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "フォールバック処理も失敗");
            }
        }
    }

    /// <summary>
    /// バッファされたチャンクを統合処理（非同期実装）
    /// SemaphoreLock競合回避とフォールバック処理
    /// </summary>
    private async Task ProcessPendingChunksAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        try
        {
            if (!await _processingLock.WaitAsync(100, cts.Token).ConfigureAwait(false))
            {
                _logger.LogWarning("SemaphoreLock競合検出 - フォールバック実行");
                await ExecuteFallbackProcessing().ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("処理タイムアウト - フォールバック実行");
            await ExecuteFallbackProcessing().ConfigureAwait(false);
            return;
        }

        try
        {
            await ProcessPendingChunksInternal().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessPendingChunksAsyncでエラー発生");
        }
        finally
        {
            _processingLock.Release();
        }
    }

    /// <summary>
    /// 緊急フォールバック処理（SemaphoreLock競合時の代替処理）
    /// </summary>
    private async Task ExecuteFallbackProcessing()
    {
        try
        {
            // ロックを取得せずに現在のチャンクを読み取り専用で処理
            var allChunks = new List<TextChunk>();

            foreach (var kvp in _pendingChunksByWindow.ToList())
            {
                var chunks = kvp.Value?.ToList() ?? [];
                if (chunks.Count > 0)
                {
                    allChunks.AddRange(chunks);
                }
            }

            if (allChunks.Count > 0)
            {
                var combinedText = string.Join(" ", allChunks.Select(c => c.CombinedText ?? "").Where(t => !string.IsNullOrWhiteSpace(t)));

                if (!string.IsNullOrWhiteSpace(combinedText))
                {
                    var fallbackChunk = new TextChunk
                    {
                        ChunkId = GenerateNewChunkId(),
                        CombinedText = combinedText,
                        CombinedBounds = allChunks.First().CombinedBounds,
                        SourceWindowHandle = allChunks.First().SourceWindowHandle,
                        DetectedLanguage = allChunks.First().DetectedLanguage,
                        TextResults = [.. allChunks.SelectMany(c => c.TextResults)],
                        CaptureRegion = allChunks.First().CaptureRegion
                    };

                    // [Issue #78 Phase 4] 画像コンテキストを含めてイベント発行
                    var imageContext = GetAndClearImageContext();
                    var aggregatedEvent = new AggregatedChunksReadyEvent(
                        new List<TextChunk> { fallbackChunk }.AsReadOnly(),
                        fallbackChunk.SourceWindowHandle,
                        imageContext.ImageBase64,
                        imageContext.Width,
                        imageContext.Height
                    );

                    await _eventAggregator.PublishAsync(aggregatedEvent).ConfigureAwait(false);
                    Interlocked.Increment(ref _totalAggregationEvents);

                    _logger.LogDebug("フォールバック処理完了: テキスト長={Length}", combinedText.Length);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "フォールバック処理でエラー発生");
            throw;
        }
    }

    /// <summary>
    /// 内部統合処理（ウィンドウハンドル別処理）
    /// </summary>
    private async Task ProcessPendingChunksInternal()
    {
        if (_pendingChunksByWindow.IsEmpty)
        {
            return;
        }

        // [Issue #228] メモリ監視ログ
        var memoryBefore = GC.GetTotalMemory(false);

        // 処理対象をアトミックに取得・削除（データロスト防止）
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
            if (allAggregatedChunks.Count > 0)
            {
                var windowHandle = allAggregatedChunks.FirstOrDefault()?.SourceWindowHandle ?? IntPtr.Zero;

                // [Issue #78 Phase 4] 画像コンテキストを含めてイベント発行
                var imageContext = GetAndClearImageContext();
                var aggregatedEvent = new AggregatedChunksReadyEvent(
                    allAggregatedChunks.AsReadOnly(),
                    windowHandle,
                    imageContext.ImageBase64,
                    imageContext.Width,
                    imageContext.Height
                );

                await _eventAggregator.PublishAsync(aggregatedEvent).ConfigureAwait(false);

                _logger.LogDebug("AggregatedChunksReadyEvent発行: ChunkCount={Count}", allAggregatedChunks.Count);
            }

            Interlocked.Increment(ref _totalAggregationEvents);

            // [Issue #228] メモリ監視ログ
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryDelta = memoryAfter - memoryBefore;
            if (memoryDelta > 10_000_000) // 10MB以上の増加を警告
            {
                _logger.LogWarning("メモリ増加検出: {DeltaMB:F1}MB (処理前: {BeforeMB:F1}MB, 処理後: {AfterMB:F1}MB)",
                    memoryDelta / 1_000_000.0, memoryBefore / 1_000_000.0, memoryAfter / 1_000_000.0);
            }

            _logger.LogDebug("統合処理完了: {InputCount}個→{OutputCount}個",
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
            var enabled = _settings.CurrentValue.ProximityGrouping.Enabled;

            // 近接度グループ化が無効の場合は従来通りの統合
            if (!enabled)
            {
                return LegacyCombineChunks(chunks);
            }

            // 近接度でグループ化
            var proximityGroups = _proximityGroupingService.GroupByProximity(chunks);

            if (proximityGroups.Count == 0)
            {
                _logger.LogWarning("グループ化結果が空 - 元のチャンクを返します");
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

            _logger.LogDebug("近接度グループ化完了: {InputCount}個→{OutputCount}個 ({GroupCount}グループ)",
                chunks.Count, combinedChunks.Count, proximityGroups.Count);

            return combinedChunks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "近接度グループ化でエラー - レガシー処理にフォールバック");
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
            return groupChunks[0];
        }

        var combinedText = _lineBreakProcessor.ProcessLineBreaks(groupChunks);
        var combinedBounds = CalculateCombinedBounds(groupChunks);

        return new TextChunk
        {
            ChunkId = GenerateNewChunkId(),
            TextResults = [.. groupChunks.SelectMany(c => c.TextResults)],
            CombinedBounds = combinedBounds,
            CombinedText = combinedText,
            SourceWindowHandle = groupChunks[0].SourceWindowHandle,
            DetectedLanguage = groupChunks[0].DetectedLanguage,
            CaptureRegion = groupChunks[0].CaptureRegion
        };
    }

    /// <summary>
    /// レガシー統合処理（近接度グループ化無効時のフォールバック）
    /// </summary>
    private List<TextChunk> LegacyCombineChunks(List<TextChunk> chunks)
    {
        var combinedText = _lineBreakProcessor.ProcessLineBreaks(chunks);
        var combinedBounds = CalculateCombinedBounds(chunks);

        var combinedChunk = new TextChunk
        {
            ChunkId = GenerateNewChunkId(),
            TextResults = [.. chunks.SelectMany(c => c.TextResults)],
            CombinedBounds = combinedBounds,
            CombinedText = combinedText,
            SourceWindowHandle = chunks[0].SourceWindowHandle,
            DetectedLanguage = chunks[0].DetectedLanguage,
            CaptureRegion = chunks[0].CaptureRegion
        };

        return [combinedChunk];
    }

    /// <summary>
    /// 統合されたバウンディングボックスを計算
    /// </summary>
    private static System.Drawing.Rectangle CalculateCombinedBounds(List<TextChunk> chunks)
    {
        if (chunks.Count == 0) return System.Drawing.Rectangle.Empty;
        if (chunks.Count == 1) return chunks[0].CombinedBounds;

        var bounds = chunks.Select(c => c.CombinedBounds).ToArray();
        var minX = bounds.Min(r => r.X);
        var minY = bounds.Min(r => r.Y);
        var maxRight = bounds.Max(r => r.Right);
        var maxBottom = bounds.Max(r => r.Bottom);

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
    /// 現在の統計情報を取得
    /// </summary>
    public (long TotalChunksProcessed, long TotalAggregationEvents) GetStatistics()
    {
        return (Interlocked.Read(ref _totalChunksProcessed), Interlocked.Read(ref _totalAggregationEvents));
    }

    /// <summary>
    /// [Issue #78 Phase 4] Cloud AI翻訳用の画像コンテキストを設定
    /// 次回のAggregatedChunksReadyEvent発行時に画像データが含まれます
    /// </summary>
    /// <param name="imageBase64">画像データ（Base64エンコード）</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    public void SetImageContext(string imageBase64, int width, int height)
    {
        lock (_imageContextLock)
        {
            _currentImageBase64 = imageBase64;
            _currentImageWidth = width;
            _currentImageHeight = height;

            _logger.LogDebug("[Issue #78] 画像コンテキスト設定: {Width}x{Height}, Base64Length={Length}",
                width, height, imageBase64?.Length ?? 0);
        }
    }

    /// <summary>
    /// [Issue #78 Phase 4] 画像コンテキストをクリア
    /// </summary>
    public void ClearImageContext()
    {
        lock (_imageContextLock)
        {
            _currentImageBase64 = null;
            _currentImageWidth = 0;
            _currentImageHeight = 0;

            _logger.LogDebug("[Issue #78] 画像コンテキストクリア");
        }
    }

    /// <summary>
    /// [Issue #78 Phase 4] 現在の画像コンテキストを取得
    /// </summary>
    private (string? ImageBase64, int Width, int Height) GetAndClearImageContext()
    {
        lock (_imageContextLock)
        {
            var result = (_currentImageBase64, _currentImageWidth, _currentImageHeight);

            // 使用後にクリア（次回のイベント発行で再利用されないように）
            _currentImageBase64 = null;
            _currentImageWidth = 0;
            _currentImageHeight = 0;

            return result;
        }
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
