using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Models.Processing;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Processing.Strategies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO;

namespace Baketa.Infrastructure.Processing;

/// <summary>
/// 段階的処理パイプラインサービスの実装
/// Infrastructure Layer - Clean Architecture準拠
/// Geminiフィードバック反映: Record型結果、詳細パフォーマンス測定、包括的エラーハンドリング
/// </summary>
public class SmartProcessingPipelineService : ISmartProcessingPipelineService, IDisposable
{
    private readonly ILogger<SmartProcessingPipelineService> _logger;
    private readonly IOptionsMonitor<ProcessingPipelineSettings> _settings;
    private readonly Dictionary<ProcessingStageType, IProcessingStageStrategy> _stageStrategies;
    private readonly object _disposeLock = new();
    private bool _disposed = false;
    
    // LoggingSettings: debug_app_logs.txtハードコード解決用
    private readonly LoggingSettings _loggingSettings;
    
    public SmartProcessingPipelineService(
        IEnumerable<IProcessingStageStrategy> strategies,
        ILogger<SmartProcessingPipelineService> logger,
        IOptionsMonitor<ProcessingPipelineSettings> settings,
        IConfiguration configuration)
    {
        try
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            // 🔥 UltraThink調査: コンストラクタ実行確認（必ずINFOレベルで出力）
            _logger.LogInformation("🔥 [CONSTRUCTOR_DEBUG] SmartProcessingPipelineService コンストラクタ開始");
            Console.WriteLine("🔥 [CONSTRUCTOR_DEBUG] SmartProcessingPipelineService コンストラクタ開始 - Console出力");
            
            // LoggingSettings初期化: debug_app_logs.txtハードコード解決用
            _loggingSettings = new LoggingSettings
            {
                DebugLogPath = configuration?.GetValue<string>("Logging:DebugLogPath") ?? "debug_app_logs.txt",
                EnableDebugFileLogging = configuration?.GetValue<bool>("Logging:EnableDebugFileLogging") ?? true,
                MaxDebugLogFileSizeMB = configuration?.GetValue<int>("Logging:MaxDebugLogFileSizeMB") ?? 10,
                DebugLogRetentionDays = configuration?.GetValue<int>("Logging:DebugLogRetentionDays") ?? 7
            };
            
            // 🔥 UltraThink調査: 注入されたパラメータをnullチェック前にログ出力
            Console.WriteLine($"🔥 [CONSTRUCTOR_DEBUG] strategies パラメータ: {(strategies == null ? "null" : "not null")}");
            _logger.LogInformation("🔥 [CONSTRUCTOR_DEBUG] strategies パラメータ: {StrategiesNull}", strategies == null ? "null" : "not null");
            
            // 🔥 UltraThink調査: 注入された戦略数確認（INFOレベル）- null チェック後
            if (strategies == null)
            {
                Console.WriteLine("🚨 [CONSTRUCTOR_ERROR] strategies が null です！");
                _logger.LogError("🚨 [CONSTRUCTOR_ERROR] strategies が null です！");
                throw new ArgumentNullException(nameof(strategies), "IEnumerable<IProcessingStageStrategy> strategies が null です");
            }
            
            var strategiesCount = strategies.Count();
            _logger.LogInformation("🔥 [CONSTRUCTOR_DEBUG] 注入された戦略数: {Count}", strategiesCount);
            Console.WriteLine($"🔥 [CONSTRUCTOR_DEBUG] 注入された戦略数: {strategiesCount}");
            
            // 🔥 UltraThink調査: 各戦略の詳細情報出力
            var strategiesList = strategies.ToList();
            for (int i = 0; i < strategiesList.Count; i++)
            {
                var strategy = strategiesList[i];
                _logger.LogInformation("🔥 [CONSTRUCTOR_DEBUG] 戦略[{Index}]: Type={Type}, StageType={StageType}", 
                    i, strategy.GetType().Name, strategy.StageType);
                Console.WriteLine($"🔥 [CONSTRUCTOR_DEBUG] 戦略[{i}]: Type={strategy.GetType().Name}, StageType={strategy.StageType}");
            }
            
            // 戦略をStageTypeでディクショナリ化（重複除去してからディクショナリ化）
            var uniqueStrategies = strategiesList.GroupBy(s => s.StageType)
                .Select(g => g.First())
                .ToList();
                
            _logger.LogInformation("🔥 [CONSTRUCTOR_DEBUG] 戦略重複除去 - 元: {Original}, 除去後: {Unique}", 
                strategiesList.Count, uniqueStrategies.Count);
            Console.WriteLine($"🔥 [CONSTRUCTOR_DEBUG] 戦略重複除去 - 元: {strategiesList.Count}, 除去後: {uniqueStrategies.Count}");
                
            _stageStrategies = uniqueStrategies.ToDictionary(s => s.StageType);
            
            _logger.LogInformation("🔥 [CONSTRUCTOR_DEBUG] 段階戦略初期化完了 - 登録数: {Count}", _stageStrategies.Count);
            Console.WriteLine($"🔥 [CONSTRUCTOR_DEBUG] 段階戦略初期化完了 - 登録数: {_stageStrategies.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 [CONSTRUCTOR_EXCEPTION] SmartProcessingPipelineService コンストラクタで例外: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"🚨 [CONSTRUCTOR_EXCEPTION] StackTrace: {ex.StackTrace}");
            
            if (logger != null)
            {
                logger.LogError(ex, "🚨 [CONSTRUCTOR_EXCEPTION] SmartProcessingPipelineService コンストラクタで例外");
            }
            
            throw; // 例外を再スロー
        }
    }

    public async Task<ProcessingPipelineResult> ExecuteAsync(ProcessingPipelineInput input, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var context = new ProcessingContext(input);
        var executedStages = new List<ProcessingStageType>();
        var stageProcessingTimes = new Dictionary<ProcessingStageType, TimeSpan>();
        
        _logger.LogDebug("段階的処理パイプライン開始 - WindowHandle: {WindowHandle}, ContextId: {ContextId}", 
            input.SourceWindowHandle, input.ContextId);
        
        // 🚨 P0システム動作確認用 - ファイルログ出力（設定外部化済み）
        if (_loggingSettings.EnableDebugFileLogging)
        {
            try
            {
                System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath), 
                    $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [P0_PIPELINE_START] SmartProcessingPipelineService.ExecuteAsync開始 - ContextId: {input.ContextId}{Environment.NewLine}");
            }
            catch { /* ファイル出力失敗は無視 */ }
        }

        // 🎯 Phase 3.11: ReferencedSafeImage参照カウント管理の初期化
        var hasReferencedSafeImage = context.HasReferencedSafeImage();
        if (hasReferencedSafeImage)
        {
            _logger.LogDebug("🎯 [PHASE3.11] ReferencedSafeImage検出 - 参照カウント管理開始, 初期カウント: {RefCount}", 
                context.GetReferenceCount());
            
            if (_loggingSettings.EnableDebugFileLogging)
            {
                try
                {
                    System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath), 
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [PHASE3.11_INIT] ReferencedSafeImage参照カウント管理開始 - 初期カウント: {context.GetReferenceCount()}{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }
        }

        try
        {
            var settings = _settings.CurrentValue;
            
            // 段階的処理が無効の場合は従来処理
            if (!settings.EnableStaging && !input.Options.EnableStaging)
            {
                _logger.LogDebug("段階的処理無効 - 従来処理モードで実行");
                
                // 🚨 P0システム動作確認用 - 従来処理ログ（設定外部化済み）
                if (_loggingSettings.EnableDebugFileLogging)
                {
                    try
                    {
                        System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath), 
                            $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🚨 [P0_LEGACY_MODE] 段階的処理無効 - 従来処理モード{Environment.NewLine}");
                    }
                    catch { /* ファイル出力失敗は無視 */ }
                }
                
                return await ExecuteLegacyModeAsync(input, cancellationToken).ConfigureAwait(false);
            }
            
            // 🚨 P0システム動作確認用 - 段階的処理有効ログ（設定外部化済み）
            if (_loggingSettings.EnableDebugFileLogging)
            {
                try
                {
                    System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath), 
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→✅ [P0_STAGING_ENABLED] 段階的処理有効 - EnableStaging: {settings.EnableStaging}, InputOptions: {input.Options.EnableStaging}{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }

            var stageOrder = GetExecutionOrder(settings);
            ProcessingStageType completedStage = ProcessingStageType.ImageChangeDetection;
            bool earlyTerminated = false;
            
            foreach (var stageType in stageOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (!_stageStrategies.TryGetValue(stageType, out var strategy))
                {
                    _logger.LogError("段階戦略が見つかりません: {StageType}", stageType);
                    continue;
                }

                // 段階実行の必要性判定
                if (!strategy.ShouldExecute(context))
                {
                    _logger.LogDebug("段階スキップ: {StageType} - 実行条件未満", stageType);
                    
                    // 早期終了判定（強制完全実行でない場合）
                    if (settings.EnableEarlyTermination && !input.Options.ForceCompleteExecution)
                    {
                        earlyTerminated = true;
                        break;
                    }
                    continue;
                }

                // 🎯 Phase 3.11: 段階開始時の参照カウント増加
                bool referenceAcquired = false;
                if (hasReferencedSafeImage)
                {
                    referenceAcquired = context.AcquireStageReference(stageType);
                    if (referenceAcquired)
                    {
                        _logger.LogDebug("🎯 [PHASE3.11] 段階参照取得成功: {StageType} - 参照カウント: {RefCount}", 
                            stageType, context.GetReferenceCount());
                        
                        if (_loggingSettings.EnableDebugFileLogging)
                        {
                            try
                            {
                                System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath), 
                                    $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [PHASE3.11_REF_ACQ] 段階参照取得: {stageType} - 参照カウント: {context.GetReferenceCount()}{Environment.NewLine}");
                            }
                            catch { /* ファイル出力失敗は無視 */ }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("🎯 [PHASE3.11] 段階参照取得失敗: {StageType} - SafeImage既に破棄済みの可能性", stageType);
                        
                        if (_loggingSettings.EnableDebugFileLogging)
                        {
                            try
                            {
                                System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath), 
                                    $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🚨 [PHASE3.11_REF_FAIL] 段階参照取得失敗: {stageType} - SafeImage破棄済み{Environment.NewLine}");
                            }
                            catch { /* ファイル出力失敗は無視 */ }
                        }
                    }
                }

                ProcessingStageResult stageResult;
                var stageStopwatch = Stopwatch.StartNew();

                try
                {
                    // 段階実行
                    _logger.LogDebug("段階実行開始: {StageType}", stageType);
                    
                    // 🚨 P0システム動作確認用 - 段階実行ログ（設定外部化済み）
                    if (_loggingSettings.EnableDebugFileLogging)
                    {
                        try
                        {
                            System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath), 
                                $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [P0_STAGE_EXEC] 段階実行開始: {stageType} - ContextId: {input.ContextId}{Environment.NewLine}");
                        }
                        catch { /* ファイル出力失敗は無視 */ }
                    }
                    
                    stageResult = await strategy.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    // 🎯 Phase 3.11: 段階完了時の参照カウント減少（例外が発生しても必ず実行）
                    if (hasReferencedSafeImage && referenceAcquired)
                    {
                        context.ReleaseStageReference(stageType);
                        _logger.LogDebug("🎯 [PHASE3.11] 段階参照解放: {StageType} - 参照カウント: {RefCount}", 
                            stageType, context.GetReferenceCount());
                        
                        if (_loggingSettings.EnableDebugFileLogging)
                        {
                            try
                            {
                                System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath), 
                                    $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [PHASE3.11_REF_REL] 段階参照解放: {stageType} - 参照カウント: {context.GetReferenceCount()}{Environment.NewLine}");
                            }
                            catch { /* ファイル出力失敗は無視 */ }
                        }
                    }
                    
                    stageStopwatch.Stop();
                    stageProcessingTimes[stageType] = stageStopwatch.Elapsed;
                }
                
                context.AddStageResult(stageType, stageResult);
                executedStages.Add(stageType);
                completedStage = stageType;

                _logger.LogDebug("段階実行完了: {StageType}, 成功: {Success}, 処理時間: {ProcessingTime}ms", 
                    stageType, stageResult.Success, stageStopwatch.Elapsed.TotalMilliseconds);

                // 段階失敗時の処理
                if (!stageResult.Success)
                {
                    _logger.LogWarning("段階処理失敗: {StageType}, エラー: {Error}", stageType, stageResult.ErrorMessage);
                    
                    if (settings.StopOnFirstError)
                    {
                        break;
                    }
                }

                // 早期終了条件チェック
                if (settings.EnableEarlyTermination && !input.Options.ForceCompleteExecution && 
                    ShouldTerminateEarly(stageType, stageResult))
                {
                    _logger.LogDebug("早期終了判定: {StageType} - 後続処理不要", stageType);
                    earlyTerminated = true;
                    break;
                }
            }

            // 🎯 Phase 3.11: パイプライン完了時の参照カウント確認
            if (hasReferencedSafeImage)
            {
                var finalRefCount = context.GetReferenceCount();
                _logger.LogInformation("🎯 [PHASE3.11] パイプライン完了 - 最終参照カウント: {RefCount}", finalRefCount);
                
                if (_loggingSettings.EnableDebugFileLogging)
                {
                    try
                    {
                        System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath), 
                            $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [PHASE3.11_FINAL] パイプライン完了 - 最終参照カウント: {finalRefCount}{Environment.NewLine}");
                    }
                    catch { /* ファイル出力失敗は無視 */ }
                }
            }

            stopwatch.Stop();
            return BuildSuccessResult(context, completedStage, stopwatch.Elapsed, executedStages, stageProcessingTimes, earlyTerminated);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("段階的処理パイプラインがキャンセルされました - ContextId: {ContextId}", input.ContextId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "段階的処理パイプライン実行エラー - ContextId: {ContextId}", input.ContextId);
            return ProcessingPipelineResult.CreateError(ex.Message, stopwatch.Elapsed, ex);
        }
    }

    public async Task<ProcessingStageResult> ExecuteStageAsync(ProcessingStageType stage, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        if (!_stageStrategies.TryGetValue(stage, out var strategy))
        {
            return ProcessingStageResult.CreateError(stage, $"段階戦略が見つかりません: {stage}");
        }

        _logger.LogDebug("単一段階実行: {StageType}", stage);
        return await strategy.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<ProcessingStageType> GetExecutableStageSuggestion(ProcessingPipelineInput input)
    {
        var settings = _settings.CurrentValue;
        var allStages = GetExecutionOrder(settings);
        var context = new ProcessingContext(input);
        var executableStages = new List<ProcessingStageType>();

        foreach (var stageType in allStages)
        {
            if (_stageStrategies.TryGetValue(stageType, out var strategy) && strategy.ShouldExecute(context))
            {
                executableStages.Add(stageType);
            }
        }

        return executableStages;
    }

    /// <summary>
    /// 早期終了判定ロジック
    /// 各段階の結果に基づいて後続処理の必要性を判定
    /// </summary>
    private bool ShouldTerminateEarly(ProcessingStageType completedStage, ProcessingStageResult stageResult)
    {
        return completedStage switch
        {
            ProcessingStageType.ImageChangeDetection => 
                stageResult.Data is ImageChangeDetectionResult imageChange && !imageChange.HasChanged,
                
            ProcessingStageType.TextChangeDetection =>
                stageResult.Data is TextChangeDetectionResult textChange && !textChange.HasTextChanged,
                
            _ => false
        };
    }

    /// <summary>
    /// 実行順序を取得
    /// </summary>
    private static List<ProcessingStageType> GetExecutionOrder(ProcessingPipelineSettings settings)
    {
        var order = new List<ProcessingStageType>
        {
            ProcessingStageType.ImageChangeDetection,
            ProcessingStageType.OcrExecution,
            ProcessingStageType.TextChangeDetection,
            ProcessingStageType.TranslationExecution
        };

        // 設定により段階順序をカスタマイズ可能
        if (settings.CustomStageOrder?.Count > 0)
        {
            return settings.CustomStageOrder.ToList();
        }

        return order;
    }

    /// <summary>
    /// 従来処理モード実行
    /// 段階的フィルタリングを使わない場合の処理
    /// </summary>
    private async Task<ProcessingPipelineResult> ExecuteLegacyModeAsync(ProcessingPipelineInput input, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var executedStages = new List<ProcessingStageType> { ProcessingStageType.OcrExecution, ProcessingStageType.TranslationExecution };
        var stageProcessingTimes = new Dictionary<ProcessingStageType, TimeSpan>();

        try
        {
            // OCR → 翻訳の順次実行（従来方式）
            _logger.LogDebug("従来処理モード実行開始");
            
            // OCR実行をシミュレート（実際は既存サービスを呼び出し）
            await Task.Delay(80, cancellationToken); // OCR処理時間をシミュレート
            stageProcessingTimes[ProcessingStageType.OcrExecution] = TimeSpan.FromMilliseconds(80);
            
            // 翻訳実行をシミュレート
            await Task.Delay(200, cancellationToken); // 翻訳処理時間をシミュレート
            stageProcessingTimes[ProcessingStageType.TranslationExecution] = TimeSpan.FromMilliseconds(200);
            
            stopwatch.Stop();
            
            return ProcessingPipelineResult.CreateSuccess(
                ProcessingStageType.TranslationExecution,
                stopwatch.Elapsed,
                executedStages,
                stageProcessingTimes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "従来処理モード実行エラー");
            return ProcessingPipelineResult.CreateError(ex.Message, stopwatch.Elapsed, ex);
        }
    }

    /// <summary>
    /// 成功結果を構築
    /// </summary>
    private ProcessingPipelineResult BuildSuccessResult(
        ProcessingContext context,
        ProcessingStageType completedStage,
        TimeSpan totalTime,
        List<ProcessingStageType> executedStages,
        Dictionary<ProcessingStageType, TimeSpan> stageProcessingTimes,
        bool earlyTerminated)
    {
        var allResults = context.GetAllStageResults();
        
        // 各段階結果を抽出
        var imageChangeResult = allResults.ContainsKey(ProcessingStageType.ImageChangeDetection) ? 
            allResults[ProcessingStageType.ImageChangeDetection].Data as ImageChangeDetectionResult : null;
        var ocrResult = allResults.ContainsKey(ProcessingStageType.OcrExecution) ? 
            allResults[ProcessingStageType.OcrExecution].Data as OcrExecutionResult : null;
        var textChangeResult = allResults.ContainsKey(ProcessingStageType.TextChangeDetection) ? 
            allResults[ProcessingStageType.TextChangeDetection].Data as TextChangeDetectionResult : null;
        var translationResult = allResults.ContainsKey(ProcessingStageType.TranslationExecution) ? 
            allResults[ProcessingStageType.TranslationExecution].Data as TranslationExecutionResult : null;

        // パフォーマンスメトリクス作成
        var metrics = new ProcessingMetrics
        {
            StageProcessingTimes = stageProcessingTimes,
            TotalStages = Enum.GetValues<ProcessingStageType>().Length,
            ExecutedStages = executedStages.Count,
            SkippedStages = Enum.GetValues<ProcessingStageType>().Length - executedStages.Count,
            EarlyTerminated = earlyTerminated,
            EstimatedCpuReduction = CalculateEstimatedCpuReduction(executedStages, stageProcessingTimes)
        };

        return new ProcessingPipelineResult
        {
            ShouldContinue = true,
            LastCompletedStage = completedStage,
            TotalElapsedTime = totalTime,
            Success = true,
            OcrResultText = ocrResult?.DetectedText,
            TranslationResultText = translationResult?.TranslatedText,
            ImageChangeResult = imageChangeResult,
            OcrResult = ocrResult,
            TextChangeResult = textChangeResult,
            TranslationResult = translationResult,
            ExecutedStages = executedStages,
            StageProcessingTimes = stageProcessingTimes,
            Metrics = metrics
        };
    }

    /// <summary>
    /// CPU削減効果を推定計算
    /// </summary>
    private static float CalculateEstimatedCpuReduction(List<ProcessingStageType> executedStages, Dictionary<ProcessingStageType, TimeSpan> stageProcessingTimes)
    {
        // 全段階実行時の推定処理時間
        var fullProcessingTime = TimeSpan.FromMilliseconds(5 + 80 + 1 + 200); // 286ms
        
        // 実際の処理時間
        var actualProcessingTime = stageProcessingTimes.Values.Aggregate(TimeSpan.Zero, (sum, time) => sum.Add(time));
        
        if (fullProcessingTime.TotalMilliseconds == 0) return 0f;
        
        var reduction = (float)(1.0 - (actualProcessingTime.TotalMilliseconds / fullProcessingTime.TotalMilliseconds));
        return Math.Max(0f, Math.Min(1f, reduction)); // 0-1の範囲でクランプ
    }


    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_disposeLock)
        {
            if (_disposed) return;
            
            try
            {
                foreach (var strategy in _stageStrategies.Values)
                {
                    if (strategy is IDisposable disposableStrategy)
                    {
                        disposableStrategy.Dispose();
                    }
                }
                
                _logger.LogDebug("SmartProcessingPipelineService disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SmartProcessingPipelineService dispose error");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}