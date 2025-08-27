using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Events.EventTypes;

namespace Baketa.Core.Events.Handlers;

/// <summary>
/// 改善版OCR完了ハンドラー - TPL Dataflow基盤の制御された並列処理実装
/// 
/// アーキテクチャ: Producer-Consumer with Controlled Parallelism
/// - BatchBlock: バッチ集約（サイズ3, タイムアウト100ms）
/// - ActionBlock: 制御された並列処理（最大並列度2）
/// - BoundedCapacity: バックプレッシャー対策
/// </summary>
public class OcrCompletedHandler_Improved : IEventProcessor<OcrCompletedEvent>, IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IUnifiedSettingsService _settingsService;
    private readonly ILogger<OcrCompletedHandler_Improved> _logger;
    
    // TPL Dataflow コンポーネント
    private readonly BatchBlock<TranslationRequestData> _batchBlock;
    private readonly ActionBlock<TranslationRequestData[]> _processBlock;
    private readonly System.Threading.Timer _batchTimer;
    private readonly CancellationTokenSource _cancellationTokenSource;
    
    // 設定値（設計書準拠）
    private const int BatchSize = 3;
    private const int BatchTimeoutMs = 100;
    private const int MaxDegreeOfParallelism = 2;
    private const int BatchBlockCapacity = 100;
    private const int ProcessBlockCapacity = 10;
    
    /// <inheritdoc />
    public int Priority => 0;
    
    /// <inheritdoc />
    public bool SynchronousExecution => false;

    public OcrCompletedHandler_Improved(
        IEventAggregator eventAggregator, 
        IUnifiedSettingsService settingsService,
        ILogger<OcrCompletedHandler_Improved> logger)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cancellationTokenSource = new CancellationTokenSource();

        // BatchBlock設定: バッチサイズ3、バックプレッシャー対応
        _batchBlock = new BatchBlock<TranslationRequestData>(
            batchSize: BatchSize,
            new GroupingDataflowBlockOptions
            {
                BoundedCapacity = BatchBlockCapacity,
                CancellationToken = _cancellationTokenSource.Token
            });

        // ActionBlock設定: 最大並列度2、制限付きキュー
        _processBlock = new ActionBlock<TranslationRequestData[]>(
            ProcessBatchAsync,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                BoundedCapacity = ProcessBlockCapacity,
                CancellationToken = _cancellationTokenSource.Token
            });

        // BatchBlock → ActionBlock のリンク
        _batchBlock.LinkTo(_processBlock, new DataflowLinkOptions { PropagateCompletion = true });

        // バッチタイムアウト用タイマー（100ms間隔）
        _batchTimer = new System.Threading.Timer(FlushBatchAsync, null, BatchTimeoutMs, BatchTimeoutMs);
        
        _logger.LogInformation("OcrCompletedHandler_Improved初期化完了 - BatchSize={BatchSize}, Timeout={TimeoutMs}ms, Parallelism={Parallelism}", 
            BatchSize, BatchTimeoutMs, MaxDegreeOfParallelism);
    }

    /// <inheritdoc />
    public async Task HandleAsync(OcrCompletedEvent eventData)
    {
        _logger.LogDebug("OcrCompletedHandler_Improved.HandleAsync開始: Results={ResultCount}", 
            eventData?.Results?.Count ?? 0);
        
        ArgumentNullException.ThrowIfNull(eventData);

        // OCR結果が存在しない場合
        if (eventData.Results == null || !eventData.Results.Any())
        {
            var notificationEvent = new NotificationEvent(
                "OCR処理は完了しましたが、テキストは検出されませんでした。",
                NotificationType.Information,
                "OCR完了");
                
            await _eventAggregator.PublishAsync(notificationEvent).ConfigureAwait(false);
            return;
        }
        
        // OCR結果を通知
        var successNotificationEvent = new NotificationEvent(
            $"OCR処理が完了しました: {eventData.Results.Count}個のテキスト領域を検出",
            NotificationType.Success,
            "OCR完了",
            displayTime: 3000);
        
        await _eventAggregator.PublishAsync(successNotificationEvent).ConfigureAwait(false);
        
        _logger.LogInformation("バッチ翻訳処理開始: {ResultCount}個のOCR結果をDataflowパイプラインに投入", 
            eventData.Results.Count);

        // 翻訳設定取得
        var translationSettings = _settingsService.GetTranslationSettings();
        var sourceLanguageCode = translationSettings.AutoDetectSourceLanguage 
            ? "auto" 
            : translationSettings.DefaultSourceLanguage;
        var targetLanguageCode = translationSettings.DefaultTargetLanguage;

        _logger.LogDebug("翻訳設定: {SourceLang} → {TargetLang} (自動検出: {AutoDetect})", 
            sourceLanguageCode, targetLanguageCode, translationSettings.AutoDetectSourceLanguage);

        // OCR結果をTranslationRequestDataに変換してバッチブロックに投入
        var enqueued = 0;
        var failed = 0;

        foreach (var result in eventData.Results)
        {
            try
            {
                var requestData = new TranslationRequestData(result, sourceLanguageCode, targetLanguageCode);
                
                // バックプレッシャー対応: SendAsyncで待機可能な投入
                var success = await _batchBlock.SendAsync(requestData, _cancellationTokenSource.Token).ConfigureAwait(false);
                
                if (success)
                {
                    enqueued++;
                    _logger.LogTrace("OCR結果をバッチキューに投入成功: '{Text}'", result.Text[..Math.Min(20, result.Text.Length)]);
                }
                else
                {
                    failed++;
                    _logger.LogWarning("OCR結果のバッチキュー投入失敗（容量制限）: '{Text}'", result.Text[..Math.Min(20, result.Text.Length)]);
                }
            }
            catch (InvalidOperationException ex)
            {
                failed++;
                _logger.LogError(ex, "OCR結果のバッチキュー投入で例外: '{Text}'", result.Text[..Math.Min(20, result.Text.Length)]);
            }
        }

        _logger.LogInformation("バッチキューへの投入完了 - 成功: {Success}, 失敗: {Failed}", enqueued, failed);
    }

    /// <summary>
    /// バッチ処理実行（ActionBlockで並列実行される）
    /// </summary>
    /// <param name="batch">処理対象のバッチ</param>
    private async Task ProcessBatchAsync(TranslationRequestData[] batch)
    {
        if (batch == null || batch.Length == 0)
        {
            return;
        }

        _logger.LogDebug("バッチ翻訳処理開始: {BatchSize}個の要求を処理", batch.Length);

        try
        {
            // バッチをBatchTranslationRequestEventとして発行
            var batchEvent = new BatchTranslationRequestEvent(
                ocrResults: batch.Select(data => data.OcrResult).ToList().AsReadOnly(),
                sourceLanguage: batch[0].SourceLanguage,
                targetLanguage: batch[0].TargetLanguage);

            await _eventAggregator.PublishAsync(batchEvent).ConfigureAwait(false);
            
            _logger.LogDebug("BatchTranslationRequestEvent発行完了: {BatchSummary}", batchEvent.BatchSummary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "バッチ翻訳処理でエラー発生: BatchSize={BatchSize}", batch.Length);
            
            // バッチ失敗時の個別フォールバック処理
            await FallbackToIndividualProcessingAsync(batch).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// バッチ失敗時の個別フォールバック処理
    /// </summary>
    /// <param name="batch">失敗したバッチ</param>
    private async Task FallbackToIndividualProcessingAsync(TranslationRequestData[] batch)
    {
        _logger.LogInformation("個別フォールバック処理開始: {BatchSize}個の要求を個別処理", batch.Length);

        foreach (var requestData in batch)
        {
            try
            {
                var individualEvent = new TranslationRequestEvent(
                    ocrResult: requestData.OcrResult,
                    sourceLanguage: requestData.SourceLanguage,
                    targetLanguage: requestData.TargetLanguage);

                await _eventAggregator.PublishAsync(individualEvent).ConfigureAwait(false);
                
                _logger.LogTrace("個別翻訳要求イベント発行完了: '{Text}'", 
                    requestData.OcrResult.Text[..Math.Min(20, requestData.OcrResult.Text.Length)]);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "個別翻訳要求でエラー: '{Text}'", 
                    requestData.OcrResult.Text[..Math.Min(20, requestData.OcrResult.Text.Length)]);
            }
        }
    }

    /// <summary>
    /// タイムアウト時のバッチフラッシュ（散発的要求対応）
    /// </summary>
    /// <param name="state">タイマー状態（未使用）</param>
    private void FlushBatchAsync(object? state)
    {
        try
        {
            // 非完了バッチを強制フラッシュ
            _batchBlock.TriggerBatch();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "バッチフラッシュでエラー");
        }
    }

    /// <summary>
    /// リソースの安全な解放
    /// </summary>
    public void Dispose()
    {
        _logger.LogInformation("OcrCompletedHandler_Improved終了処理開始");
        
        try
        {
            // タイマー停止
            _batchTimer?.Dispose();
            
            // キャンセレーション実行
            _cancellationTokenSource.Cancel();
            
            // Dataflowブロックの完了と待機
            _batchBlock.Complete();
            Task.WaitAll([_processBlock.Completion], TimeSpan.FromSeconds(5));
            
            _cancellationTokenSource.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OcrCompletedHandler_Improved終了処理でエラー");
        }
        finally
        {
            _logger.LogInformation("OcrCompletedHandler_Improved終了処理完了");
        }
        
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 翻訳要求データ（内部用）
/// </summary>
/// <param name="ocrResult">OCR結果</param>
/// <param name="sourceLanguage">ソース言語</param>
/// <param name="targetLanguage">ターゲット言語</param>
internal readonly record struct TranslationRequestData(
    Models.OCR.OcrResult OcrResult, 
    string SourceLanguage, 
    string TargetLanguage);