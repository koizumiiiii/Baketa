using System.Diagnostics;
using System.IO;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Models.Processing;
using Baketa.Core.Settings;
using Baketa.Core.Abstractions.OCR; // [Issue #392] OcrTextRegion参照
using Baketa.Core.Abstractions.Translation; // [Issue #392] TextChunk参照
using Baketa.Infrastructure.Processing.Strategies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IPipelineExecutionManager _pipelineExecutionManager;
    private readonly object _disposeLock = new();
    private bool _disposed = false;

    // LoggingSettings: debug_app_logs.txtハードコード解決用
    private readonly LoggingSettings _loggingSettings;

    public SmartProcessingPipelineService(
        IEnumerable<IProcessingStageStrategy> strategies,
        ILogger<SmartProcessingPipelineService> logger,
        IOptionsMonitor<ProcessingPipelineSettings> settings,
        IPipelineExecutionManager pipelineExecutionManager,
        IConfiguration configuration)
    {
        try
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _pipelineExecutionManager = pipelineExecutionManager ?? throw new ArgumentNullException(nameof(pipelineExecutionManager));

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

            logger?.LogError(ex, "🚨 [CONSTRUCTOR_EXCEPTION] SmartProcessingPipelineService コンストラクタで例外");

            throw; // 例外を再スロー
        }
    }

    public async Task<ProcessingPipelineResult> ExecuteAsync(ProcessingPipelineInput input, CancellationToken cancellationToken = default)
    {
        // 🔥🔥🔥 [ULTRA_DEBUG] ExecuteAsync最初のログ
        try
        {
            System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}→🔥🔥🔥 [PIPELINE_START] SmartProcessingPipelineService.ExecuteAsync開始{Environment.NewLine}");
        }
        catch { /* ログ失敗は無視 */ }

        // 🎯 [STRATEGY_A] 並行パイプライン実行を防ぐ排他制御でラップ
        return await _pipelineExecutionManager.ExecuteExclusivelyAsync(async (ct) =>
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
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [STRATEGY_A] SmartProcessingPipelineService.ExecuteAsync開始 - ContextId: {input.ContextId}{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }

            // 🎯 Phase 3.2A: PipelineScope管理の初期化 (Gemini推奨実装)
            var pipelineScope = context.CreatePipelineScope();
            if (pipelineScope != null)
            {
                _logger.LogInformation("🎯 [STRATEGY_A] PipelineScope作成成功 - Baseline Reference確保, 初期参照カウント: {RefCount}",
                    context.GetReferenceCount());

                if (_loggingSettings.EnableDebugFileLogging)
                {
                    try
                    {
                        System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                            $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [STRATEGY_A] PipelineScope Baseline Reference確保 - 初期参照カウント: {context.GetReferenceCount()}{Environment.NewLine}");
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
                                $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🚨 [STRATEGY_A] 段階的処理無効 - 従来処理モード{Environment.NewLine}");
                        }
                        catch { /* ファイル出力失敗は無視 */ }
                    }

                    return await ExecuteLegacyModeAsync(input, ct).ConfigureAwait(false);
                }

                // 🚨 P0システム動作確認用 - 段階的処理有効ログ（設定外部化済み）
                if (_loggingSettings.EnableDebugFileLogging)
                {
                    try
                    {
                        System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                            $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→✅ [STRATEGY_A] 段階的処理有効 - EnableStaging: {settings.EnableStaging}, InputOptions: {input.Options.EnableStaging}{Environment.NewLine}");
                    }
                    catch { /* ファイル出力失敗は無視 */ }
                }

                var stageOrder = GetExecutionOrder(settings, input.Options);
                ProcessingStageType completedStage = ProcessingStageType.ImageChangeDetection;
                bool earlyTerminated = false;

                foreach (var stageType in stageOrder)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!_stageStrategies.TryGetValue(stageType, out var strategy))
                    {
                        _logger.LogError("段階戦略が見つかりません: {StageType}", stageType);
                        continue;
                    }

                    // 🔥 [PHASE2.2.1] キャプチャ段階でOCRが既に実行済みの場合、OCR段階をスキップ
                    if (stageType == ProcessingStageType.OcrExecution && input.PreExecutedOcrResult != null)
                    {
                        _logger.LogInformation("🔥 [PHASE2.2.1] OCR段階スキップ - キャプチャ時にOCR実行済み (FullScreenOcrCaptureStrategy), Regions: {RegionCount}",
                            input.PreExecutedOcrResult.TextRegions.Count);

                        // 🚀 [Issue #193] OcrResults → OcrExecutionResult変換（座標スケーリング済み結果を下流に伝達）
                        // PreExecutedOcrResultはOcrResults型だが、下流のBuildSuccessResultはOcrExecutionResult型を期待する
                        var preOcrResult = input.PreExecutedOcrResult;
                        var detectedText = string.Join(" ", preOcrResult.TextRegions.Select(r => r.Text));
                        var textChunks = preOcrResult.TextRegions.Cast<object>().ToList();

                        var convertedOcrResult = new OcrExecutionResult
                        {
                            DetectedText = detectedText,
                            TextChunks = textChunks,
                            ProcessingTime = preOcrResult.ProcessingTime,
                            Success = true
                        };

                        // OCR結果をcontextに格納（後続の翻訳段階で使用）
                        var skippedResult = ProcessingStageResult.CreateSkipped(
                            ProcessingStageType.OcrExecution,
                            $"FullScreenOcrCaptureStrategyでOCR実行済み ({input.PreExecutedOcrResult.TextRegions.Count} regions)");
                        skippedResult = skippedResult with { Data = convertedOcrResult };
                        context.AddStageResult(ProcessingStageType.OcrExecution, skippedResult);

                        executedStages.Add(stageType);
                        continue;
                    }

                    // 🔧 [SINGLESHOT_FIX] ForceCompleteExecution時はShouldExecute判定を無視
                    // 段階実行の必要性判定
                    var shouldExecute = strategy.ShouldExecute(context);

                    // 🔥🔥🔥 [ULTRA_DEBUG] ShouldExecute結果
                    try
                    {
                        System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}→🔥🔥🔥 [PIPELINE_FLOW] {stageType}.ShouldExecute = {shouldExecute}{Environment.NewLine}");
                    }
                    catch { /* ログ失敗は無視 */ }

                    if (!input.Options.ForceCompleteExecution && !shouldExecute)
                    {
                        _logger.LogDebug("段階スキップ: {StageType} - 実行条件未満", stageType);

                        // 早期終了判定（強制完全実行でない場合）
                        if (settings.EnableEarlyTermination)
                        {
                            earlyTerminated = true;
                            break;
                        }
                        continue;
                    }

                    // 🔧 [SINGLESHOT_FIX] ForceCompleteExecution時の強制実行ログ
                    if (input.Options.ForceCompleteExecution && !strategy.ShouldExecute(context))
                    {
                        _logger.LogInformation("🔧 [SINGLESHOT_FIX] ForceCompleteExecution有効 - {StageType}段階を強制実行", stageType);
                    }

                    // 🎯 Strategy A: 段階開始時の一時参照取得 (PipelineScope使用)
                    TemporaryReferenceScope? temporaryReference = null;
                    if (pipelineScope != null)
                    {
                        try
                        {
                            temporaryReference = pipelineScope.AcquireTemporaryReference();
                            if (temporaryReference.IsReferenceValid)
                            {
                                _logger.LogDebug("🎯 [STRATEGY_A] 段階一時参照取得成功: {StageType} - 参照カウント: {RefCount}",
                                    stageType, context.GetReferenceCount());

                                if (_loggingSettings.EnableDebugFileLogging)
                                {
                                    try
                                    {
                                        System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                                            $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [STRATEGY_A] 段階一時参照取得: {stageType} - 参照カウント: {context.GetReferenceCount()}{Environment.NewLine}");
                                    }
                                    catch { /* ファイル出力失敗は無視 */ }
                                }
                            }
                            else
                            {
                                _logger.LogWarning("🎯 [STRATEGY_A] 段階一時参照無効: {StageType} - SafeImage状態異常", stageType);

                                if (_loggingSettings.EnableDebugFileLogging)
                                {
                                    try
                                    {
                                        System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                                            $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🚨 [STRATEGY_A] 段階一時参照無効: {stageType} - SafeImage状態異常{Environment.NewLine}");
                                    }
                                    catch { /* ファイル出力失敗は無視 */ }
                                }
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            _logger.LogError("🎯 [STRATEGY_A] PipelineScope破棄済み: {StageType} - 排他制御により防止されるべき状況", stageType);

                            if (_loggingSettings.EnableDebugFileLogging)
                            {
                                try
                                {
                                    System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🚨 [STRATEGY_A] PipelineScope破棄済み: {stageType} - 排他制御により防止されるべき状況{Environment.NewLine}");
                                }
                                catch { /* ファイル出力失敗は無視 */ }
                            }
                            break; // パイプライン中断
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
                                    $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [STRATEGY_A] 段階実行開始: {stageType} - ContextId: {input.ContextId}{Environment.NewLine}");
                            }
                            catch { /* ファイル出力失敗は無視 */ }
                        }

                        stageResult = await strategy.ExecuteAsync(context, ct).ConfigureAwait(false);

                        // 🔥🔥🔥 [ULTRA_DEBUG] strategy.ExecuteAsync完了
                        try
                        {
                            System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}→🔥🔥🔥 [PIPELINE_FLOW] {stageType}段階完了 - Success: {stageResult.Success}, Data型: {stageResult.Data?.GetType().Name ?? "NULL"}{Environment.NewLine}");
                        }
                        catch { /* ログ失敗は無視 */ }
                    }
                    finally
                    {
                        // 🎯 Strategy A: 段階完了時の一時参照解放（例外が発生しても必ず実行）
                        if (temporaryReference != null)
                        {
                            temporaryReference.Dispose();
                            _logger.LogDebug("🎯 [STRATEGY_A] 段階一時参照解放: {StageType} - 参照カウント: {RefCount}",
                                stageType, context.GetReferenceCount());

                            if (_loggingSettings.EnableDebugFileLogging)
                            {
                                try
                                {
                                    System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [STRATEGY_A] 段階一時参照解放: {stageType} - 参照カウント: {context.GetReferenceCount()}{Environment.NewLine}");
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

                    // [Issue #392] OCR完了後、テキスト位置をImageChangeDetectionStageStrategyにフィードバック
                    if (stageType == ProcessingStageType.OcrExecution && stageResult.Success && stageResult.Data is OcrExecutionResult ocrResult)
                    {
                        FeedbackOcrTextBoundsToDetectionStrategy(input, ocrResult);
                        // [Issue #500] Note: Detection矩形キャッシュはOcrExecutionStageStrategy.TrySkipWithDetectionOnlyAsyncが
                        // Detection-Only結果で直接更新する。フルOCR TextChunks（集約後）とは矩形数が異なるため、
                        // ここではキャッシュを上書きしない。
                    }

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

                        // 🔥🔥🔥 [ULTRA_DEBUG] 早期終了
                        try
                        {
                            System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}→🔥🔥🔥 [PIPELINE_FLOW] 早期終了判定 - {stageType}で終了{Environment.NewLine}");
                        }
                        catch { /* ログ失敗は無視 */ }

                        earlyTerminated = true;
                        break;
                    }

                    // 🔥🔥🔥 [ULTRA_DEBUG] 次段階へ
                    try
                    {
                        System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}→🔥🔥🔥 [PIPELINE_FLOW] {stageType}完了 - 次段階へ続行{Environment.NewLine}");
                    }
                    catch { /* ログ失敗は無視 */ }
                }

                // 🎯 Strategy A: パイプライン完了時の参照カウント確認
                if (pipelineScope != null)
                {
                    var finalRefCount = context.GetReferenceCount();
                    _logger.LogInformation("🎯 [STRATEGY_A] パイプライン完了 - 最終参照カウント: {RefCount}", finalRefCount);

                    if (_loggingSettings.EnableDebugFileLogging)
                    {
                        try
                        {
                            System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                                $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [STRATEGY_A] パイプライン完了 - 最終参照カウント: {finalRefCount}{Environment.NewLine}");
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
            finally
            {
                // 🎯 Strategy A: PipelineScope Baseline Reference解放（最外層finally）
                if (pipelineScope != null)
                {
                    try
                    {
                        pipelineScope.Dispose();
                        _logger.LogInformation("🎯 [STRATEGY_A] PipelineScope Baseline Reference解放完了 - 最終参照カウント: {RefCount}",
                            context.GetReferenceCount());

                        if (_loggingSettings.EnableDebugFileLogging)
                        {
                            try
                            {
                                System.IO.File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _loggingSettings.DebugLogPath),
                                    $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🎯 [STRATEGY_A] PipelineScope Baseline Reference解放完了{Environment.NewLine}");
                            }
                            catch { /* ファイル出力失敗は無視 */ }
                        }
                    }
                    catch (Exception disposeEx)
                    {
                        _logger.LogWarning(disposeEx, "🎯 [STRATEGY_A] PipelineScope解放中にエラー発生（処理は継続）");
                    }
                }
            }
        }, cancellationToken).ConfigureAwait(false);
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
        var allStages = GetExecutionOrder(settings, input.Options);
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
        // [Issue #302 DEBUG] 詳細ログ追加 - 早期終了判定の原因特定
        if (completedStage == ProcessingStageType.ImageChangeDetection)
        {
            if (stageResult.Data is ImageChangeDetectionResult imageChange)
            {
                var shouldTerminate = !imageChange.HasChanged;
                _logger.LogInformation("🔍 [EARLY_TERM_DEBUG] ImageChangeDetection早期終了判定: HasChanged={HasChanged}, ChangePercentage={ChangePercentage:F4}, ShouldTerminate={ShouldTerminate}",
                    imageChange.HasChanged, imageChange.ChangePercentage, shouldTerminate);
                return shouldTerminate;
            }
            else
            {
                _logger.LogWarning("🔍 [EARLY_TERM_DEBUG] ImageChangeDetection結果がnullまたは型不一致: DataType={DataType}",
                    stageResult.Data?.GetType().Name ?? "null");
                return false;
            }
        }

        return completedStage switch
        {
            ProcessingStageType.TextChangeDetection =>
                stageResult.Data is TextChangeDetectionResult textChange && !textChange.HasTextChanged,

            _ => false
        };
    }

    /// <summary>
    /// 実行順序を取得
    /// UltraThink Phase 3: 個別翻訳実行時の統合翻訳スキップ対応
    /// </summary>
    private static List<ProcessingStageType> GetExecutionOrder(ProcessingPipelineSettings settings, ProcessingPipelineOptions? options = null)
    {
        // 🔥 [Phase 12.2] TranslationExecutionStageStrategy削除により、TranslationExecution段階も削除
        var order = new List<ProcessingStageType>
        {
            ProcessingStageType.ImageChangeDetection,
            ProcessingStageType.OcrExecution,
            ProcessingStageType.TextChangeDetection
            // TranslationExecution段階は削除済み（新アーキテクチャでは翻訳は別経路で実行）
        };

        // UltraThink Phase 3: 個別翻訳実行時は統合翻訳をスキップ（削除済みのため不要）
        // if (options?.SkipIntegratedTranslation == true)
        // {
        //     order.Remove(ProcessingStageType.TranslationExecution);
        // }

        // 設定により段階順序をカスタマイズ可能
        if (settings.CustomStageOrder?.Count > 0)
        {
            var customOrder = settings.CustomStageOrder.ToList();

            // カスタム順序でも個別翻訳時は統合翻訳をスキップ（削除済みのため不要）
            // if (options?.SkipIntegratedTranslation == true)
            // {
            //     customOrder.Remove(ProcessingStageType.TranslationExecution);
            // }

            return customOrder;
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

    /// <summary>
    /// [Issue #392] OCR結果のテキスト位置をImageChangeDetectionStageStrategyにフィードバック
    /// 次サイクルのIsTextDisappearance()で「前回テキストがあった場所が変わったか」を判定するために使用
    /// </summary>
    private void FeedbackOcrTextBoundsToDetectionStrategy(ProcessingPipelineInput input, OcrExecutionResult ocrResult)
    {
        try
        {
            if (!_stageStrategies.TryGetValue(ProcessingStageType.ImageChangeDetection, out var strategy)
                || strategy is not ImageChangeDetectionStageStrategy detectionStrategy)
            {
                return;
            }

            var contextId = ImageChangeDetectionStageStrategy.BuildContextId(
                input.SourceWindowHandle, input.CaptureRegion);

            // OcrExecutionResult.TextChunksはList<object>で、
            // 実際にはOcrTextRegion（画像ピクセル座標）が格納されている。
            // ただしOcrTextRegion.BoundsはOcrExecutionStageStrategyで
            // OriginalWindowSizeにスケーリング済み（Issue #193）の場合がある。
            // ChangedRegionsはCapturedImage座標系（リサイズ後）なので、
            // TextBoundsをCapturedImage座標系に逆スケーリングする必要がある。
            var textBounds = ocrResult.TextChunks
                .OfType<OcrTextRegion>()
                .Select(region => region.Bounds)
                .Where(bounds => bounds.Width > 0 && bounds.Height > 0)
                .ToArray();

            // [Issue #392] OriginalWindowSize→CapturedImage座標への逆スケーリング
            // OcrExecutionStageStrategyがIssue #193でOriginalWindowSizeにスケール済みの場合、
            // ChangedRegionsはCapturedImage座標系なのでスケールを戻す
            if (textBounds.Length > 0 && input.CapturedImage != null)
            {
                var capturedWidth = input.CapturedImage.Width;
                var capturedHeight = input.CapturedImage.Height;
                var originalSize = input.OriginalWindowSize;

                if (originalSize.Width > 0 && originalSize.Height > 0 &&
                    (originalSize.Width != capturedWidth || originalSize.Height != capturedHeight))
                {
                    var invScaleX = (double)capturedWidth / originalSize.Width;
                    var invScaleY = (double)capturedHeight / originalSize.Height;

                    _logger.LogDebug(
                        "[Issue #392] TextBounds逆スケーリング: Original={OW}x{OH} → Captured={CW}x{CH}, InvScale=({SX:F3},{SY:F3})",
                        originalSize.Width, originalSize.Height, capturedWidth, capturedHeight, invScaleX, invScaleY);

                    textBounds = textBounds.Select(b => new System.Drawing.Rectangle(
                        (int)(b.X * invScaleX),
                        (int)(b.Y * invScaleY),
                        Math.Max(1, (int)(b.Width * invScaleX)),
                        Math.Max(1, (int)(b.Height * invScaleY))
                    )).ToArray();
                }
            }

            detectionStrategy.UpdatePreviousTextBounds(contextId, textBounds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #392] OCRテキスト位置フィードバック失敗");
        }
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
