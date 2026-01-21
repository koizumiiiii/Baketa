using System.Diagnostics;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services;

/// <summary>
/// ローディング画面初期化サービス
/// ローディング画面で実行される5つの初期化ステップを管理します
/// [Issue #185] 初回起動時のコンポーネントダウンロードステップを追加
/// [Issue #193] GPU環境自動セットアップステップを追加
/// [Issue #198] 初期化完了シグナルを追加（翻訳サーバー起動の遅延制御）
/// </summary>
public class ApplicationInitializer : ILoadingScreenInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApplicationInitializer> _logger;
    private readonly IComponentDownloader? _componentDownloader;
    private readonly IGpuEnvironmentService? _gpuEnvironmentService;
    private readonly IInitializationCompletionSignal? _completionSignal;
    private readonly IEventAggregator? _eventAggregator;
    private readonly Stopwatch _stopwatch = new();

    /// <inheritdoc/>
    public event EventHandler<LoadingProgressEventArgs>? ProgressChanged;

    public ApplicationInitializer(
        IServiceProvider serviceProvider,
        ILogger<ApplicationInitializer> logger,
        IComponentDownloader? componentDownloader = null,
        IGpuEnvironmentService? gpuEnvironmentService = null,
        IInitializationCompletionSignal? completionSignal = null,
        IEventAggregator? eventAggregator = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _componentDownloader = componentDownloader;
        _gpuEnvironmentService = gpuEnvironmentService;
        _completionSignal = completionSignal;
        _eventAggregator = eventAggregator;

        // [Issue #185] デバッグ: IComponentDownloader注入状況確認
        _logger.LogDebug("[Issue185] ApplicationInitializer コンストラクタ実行");
        _logger.LogDebug("[Issue185] IComponentDownloader is null: {IsNull}", _componentDownloader == null);
        if (_componentDownloader != null)
        {
            _logger.LogDebug("[Issue185] IComponentDownloader Type: {Type}", _componentDownloader.GetType().FullName);
        }

        // Subscribe to download progress events
        if (_componentDownloader != null)
        {
            _componentDownloader.DownloadProgressChanged += OnDownloadProgressChanged;
            _logger.LogDebug("[Issue185] DownloadProgressChanged イベント購読完了");
        }
        else
        {
            _logger.LogWarning("[Issue185] IComponentDownloaderがnullのためイベント購読スキップ");
        }

        // [Issue #193] GPU環境サービスのイベント購読
        if (_gpuEnvironmentService != null)
        {
            _gpuEnvironmentService.ProgressChanged += OnGpuSetupProgressChanged;
            _logger.LogDebug("[Issue193] GpuEnvironmentService.ProgressChanged イベント購読完了");
        }
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _stopwatch.Start();
        _logger.LogInformation("アプリケーション初期化開始");

        try
        {
            // [Issue #213] Phase 1: ダウンロードとGPUセットアップを並列実行
            // これらは独立した処理のため、並列化することで起動時間を短縮
            _logger.LogInformation("[Issue #213] Phase 1: コンポーネントダウンロードとGPUセットアップを並列実行");
            ReportProgress("parallel_init", "初期化中（ダウンロード + GPU環境セットアップ）...", isCompleted: false, progress: 0);

            var downloadTask = ExecuteStepAsync(
                "download_components",
                "コンポーネントをダウンロードしています...",
                DownloadMissingComponentsAsync,
                cancellationToken);

            var gpuSetupTask = ExecuteStepAsync(
                "setup_gpu",
                "GPU環境をチェックしています...",
                SetupGpuEnvironmentAsync,
                cancellationToken);

            await Task.WhenAll(downloadTask, gpuSetupTask).ConfigureAwait(false);
            _logger.LogInformation("[Issue #213] Phase 1 完了");

            // [Issue #213] Phase 2: 依存関係解決（シーケンシャル - サービス登録に依存）
            await ExecuteStepAsync(
                "resolve_dependencies",
                "依存関係を解決しています...",
                ResolveDependenciesAsync,
                cancellationToken).ConfigureAwait(false);

            // [Issue #292] 初期化完了シグナルをPhase 3の前に発行
            // ServerManagerHostedServiceが統合サーバーを起動できるようにする
            // これにより、OCR初期化時に統合サーバーが利用可能になる
            // 注意: 元々はPhase 4の後に呼び出していたが、循環依存回避のため前倒し
            _completionSignal?.SignalCompletion();
            _logger.LogInformation("[Issue #292] 初期化完了シグナルを発行（Phase 3の前）- 統合サーバー起動を許可");

            // [Issue #213] Phase 3: OCRと翻訳エンジンを並列初期化
            // これらは独立しており、並列化することで初期化時間を短縮
            _logger.LogInformation("[Issue #213] Phase 3: OCRと翻訳エンジンを並列初期化");
            ReportProgress("parallel_engines", "OCR・翻訳エンジンを初期化中...", isCompleted: false, progress: 50);

            var ocrTask = ExecuteStepAsync(
                "load_ocr",
                "OCRモデルを読み込んでいます...",
                InitializeOcrAsync,
                cancellationToken);

            var translationTask = ExecuteStepAsync(
                "init_translation",
                "翻訳エンジンを初期化しています...",
                InitializeTranslationAsync,
                cancellationToken);

            await Task.WhenAll(ocrTask, translationTask).ConfigureAwait(false);
            _logger.LogInformation("[Issue #213] Phase 3 完了");

            // Step 4: UIコンポーネント準備（最後に実行）
            await ExecuteStepAsync(
                "prepare_ui",
                "UIコンポーネントを準備しています...",
                PrepareUIComponentsAsync,
                cancellationToken).ConfigureAwait(false);

            _stopwatch.Stop();
            _logger.LogInformation(
                "アプリケーション初期化完了: {ElapsedMs}ms（並列化により最適化済み）",
                _stopwatch.ElapsedMilliseconds);

            // [Issue #292] SignalCompletion()はPhase 2の後（Phase 3の前）に移動済み
            // 循環依存回避のため、OCR初期化前に統合サーバーを起動可能にする必要がある
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            _logger.LogError(
                ex,
                "アプリケーション初期化失敗: {ElapsedMs}ms",
                _stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// 初期化ステップを実行し、進捗をレポートします
    /// </summary>
    private async Task ExecuteStepAsync(
        string stepId,
        string message,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var stepStopwatch = Stopwatch.StartNew();

        // ステップ開始を通知
        ReportProgress(stepId, message, isCompleted: false, progress: 0);

        try
        {
            await action(cancellationToken).ConfigureAwait(false);
            stepStopwatch.Stop();

            _logger.LogInformation(
                "ステップ完了: {StepId} ({ElapsedMs}ms)",
                stepId,
                stepStopwatch.ElapsedMilliseconds);

            // ステップ完了を通知
            ReportProgress(stepId, message, isCompleted: true, progress: 100);
        }
        catch (Exception ex)
        {
            stepStopwatch.Stop();
            _logger.LogError(
                ex,
                "ステップ失敗: {StepId} ({ElapsedMs}ms)",
                stepId,
                stepStopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// 進捗をレポートします
    /// </summary>
    private void ReportProgress(string stepId, string message, bool isCompleted, int progress)
    {
        ProgressChanged?.Invoke(this, new LoadingProgressEventArgs
        {
            StepId = stepId,
            Message = message,
            IsCompleted = isCompleted,
            Progress = progress
        });
    }

    /// <summary>
    /// Step 1: 依存関係を解決します
    /// </summary>
    private async Task ResolveDependenciesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("依存関係解決開始");

        // 必要なサービスが取得できることを確認
        _ = _serviceProvider.GetRequiredService<ILogger<ApplicationInitializer>>();

        // 非同期処理をシミュレート（実際の処理がある場合はここに実装）
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("依存関係解決完了");
    }

    /// <summary>
    /// Step 2: OCRエンジンを初期化します
    /// Issue #189: Surya OCRサーバー自動起動対応
    /// </summary>
    private async Task InitializeOcrAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OCRエンジン初期化開始");

        var ocrEngine = _serviceProvider.GetService<Baketa.Core.Abstractions.OCR.IOcrEngine>();
        if (ocrEngine != null)
        {
            _logger.LogInformation("OCRエンジン検出: {EngineName}", ocrEngine.EngineName);
            Console.WriteLine($"🔧 [OCR] OCRエンジン検出: {ocrEngine.EngineName}");

            var initialized = await ocrEngine.InitializeAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            if (initialized)
            {
                _logger.LogInformation("✅ OCRエンジン初期化成功: {EngineName}", ocrEngine.EngineName);
                Console.WriteLine($"✅ [OCR] OCRエンジン初期化成功: {ocrEngine.EngineName}");
            }
            else
            {
                _logger.LogWarning("⚠️ OCRエンジン初期化失敗: {EngineName} - アプリケーションは継続します", ocrEngine.EngineName);
                Console.WriteLine($"⚠️ [OCR] OCRエンジン初期化失敗: {ocrEngine.EngineName}");
                // 初期化失敗は致命的エラーではない（後で再試行可能）
            }
        }
        else
        {
            _logger.LogWarning("IOcrEngineが登録されていません - OCR機能は使用できません");
            Console.WriteLine("⚠️ [OCR] IOcrEngineが登録されていません");
        }

        _logger.LogInformation("OCRエンジン初期化処理完了");
    }

    /// <summary>
    /// Step 3: 翻訳エンジンを初期化します
    /// </summary>
    private async Task InitializeTranslationAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("翻訳エンジン初期化開始");

        // TODO: Issue #170完了後、実際の翻訳エンジン初期化を実装
        // var translationService = _serviceProvider.GetService<ITranslationService>();
        // if (translationService != null)
        // {
        //     await translationService.WarmUpAsync(cancellationToken).ConfigureAwait(false);
        // }

        // 暫定的にダミー処理
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("翻訳エンジン初期化完了");
    }

    /// <summary>
    /// Step 4: UIコンポーネントを準備します
    /// </summary>
    private async Task PrepareUIComponentsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("UIコンポーネント準備開始");

        // 非同期処理をシミュレート（実際の処理がある場合はここに実装）
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("UIコンポーネント準備完了");
    }

    /// <summary>
    /// [Issue #185] Step 0: 不足コンポーネントをダウンロードします
    /// 既にインストール済みの場合はスキップします
    /// </summary>
    private async Task DownloadMissingComponentsAsync(CancellationToken cancellationToken)
    {
        if (_componentDownloader == null)
        {
            _logger.LogInformation("コンポーネントダウンローダーが未設定のため、ダウンロードステップをスキップ");
            return;
        }

        _logger.LogInformation("コンポーネントダウンロードチェック開始");

        try
        {
            var downloadCount = await _componentDownloader
                .DownloadMissingComponentsAsync(cancellationToken)
                .ConfigureAwait(false);

            if (downloadCount > 0)
            {
                _logger.LogInformation("{Count}個のコンポーネントをダウンロードしました", downloadCount);
            }
            else
            {
                _logger.LogInformation("全てのコンポーネントは既にインストール済みです");
            }

            // [Issue #185] NLLB tokenizer.json 補完ダウンロード
            // CTranslate2モデルパッケージにtokenizer.jsonが含まれていない場合、
            // HuggingFaceから自動ダウンロードする
            try
            {
                var tokenizerDownloaded = await _componentDownloader
                    .EnsureNllbTokenizerAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (tokenizerDownloaded)
                {
                    _logger.LogInformation("[Issue #185] NLLB tokenizer.json をHuggingFaceからダウンロードしました");
                }
            }
            catch (Exception tokenizerEx)
            {
                _logger.LogWarning(tokenizerEx, "[Issue #185] tokenizer.jsonダウンロードに失敗しましたが、続行します");
            }
        }
        catch (AggregateException aggEx)
        {
            // [Gemini Review] 必須コンポーネントの失敗 - ユーザーに再起動を促す
            _logger.LogWarning(aggEx, "必須コンポーネントのダウンロードに失敗しました");
            await PublishDownloadFailedEventAsync(aggEx, hasRequiredFailures: true).ConfigureAwait(false);
            // 続行するが、ユーザーには再起動を促す通知が表示される
        }
        catch (Exception ex)
        {
            // [Gemini Review] オプションコンポーネントの失敗 - ユーザーに通知
            _logger.LogWarning(ex, "コンポーネントダウンロードに失敗しましたが、続行します");
            await PublishDownloadFailedEventAsync(ex, hasRequiredFailures: false).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// [Gemini Review] ダウンロード失敗イベントを発行
    /// UIでユーザーに再起動を促す通知を表示
    /// </summary>
    private async Task PublishDownloadFailedEventAsync(Exception exception, bool hasRequiredFailures)
    {
        if (_eventAggregator == null)
        {
            _logger.LogDebug("EventAggregatorが未設定のため、ダウンロード失敗イベントをスキップ");
            return;
        }

        var failedIds = new List<string>();
        var errorMessage = exception.Message;

        // AggregateExceptionから失敗したコンポーネントIDを抽出
        if (exception is AggregateException aggEx)
        {
            foreach (var innerEx in aggEx.InnerExceptions)
            {
                if (innerEx is ComponentDownloadException downloadEx)
                {
                    failedIds.Add(downloadEx.ComponentId);
                }
            }
            errorMessage = string.Join(", ", aggEx.InnerExceptions.Select(e => e.Message));
        }
        else if (exception is ComponentDownloadException downloadEx)
        {
            failedIds.Add(downloadEx.ComponentId);
        }

        var downloadFailedEvent = new ComponentDownloadFailedEvent(
            failedIds.AsReadOnly(),
            hasRequiredFailures,
            errorMessage);

        try
        {
            await _eventAggregator.PublishAsync(downloadFailedEvent).ConfigureAwait(false);
            _logger.LogInformation("[Gemini Review] ComponentDownloadFailedEvent発行完了 (Required: {HasRequired}, Failed: {FailedCount})",
                hasRequiredFailures, failedIds.Count);
        }
        catch (Exception pubEx)
        {
            _logger.LogWarning(pubEx, "ダウンロード失敗イベントの発行に失敗");
        }
    }

    /// <summary>
    /// [Issue #193] GPU環境をセットアップします
    /// 開発版でのみ実行され、NVIDIA GPU検出時にユーザー確認後GPU版パッケージをインストール
    /// </summary>
    private async Task SetupGpuEnvironmentAsync(CancellationToken cancellationToken)
    {
        if (_gpuEnvironmentService == null)
        {
            _logger.LogInformation("[Issue #193] GPU環境サービスが未設定のため、GPUセットアップをスキップ");
            return;
        }

        _logger.LogInformation("[Issue #193] GPU環境セットアップ開始");

        try
        {
            var result = await _gpuEnvironmentService
                .EnsureGpuEnvironmentAsync(cancellationToken)
                .ConfigureAwait(false);

            switch (result)
            {
                case GpuSetupResult.Success:
                    _logger.LogInformation("[Issue #193] GPU環境セットアップ成功");
                    break;
                case GpuSetupResult.AlreadySetup:
                    _logger.LogInformation("[Issue #193] GPU環境は既にセットアップ済み");
                    break;
                case GpuSetupResult.NoNvidiaGpu:
                    _logger.LogInformation("[Issue #193] NVIDIA GPU未検出 - CPUモードで続行");
                    break;
                case GpuSetupResult.Skipped:
                    _logger.LogInformation("[Issue #193] GPUセットアップをスキップ - CPUモードで続行");
                    break;
                case GpuSetupResult.InstallationFailed:
                    _logger.LogWarning("[Issue #193] GPUパッケージインストール失敗 - CPUモードで続行");
                    break;
                case GpuSetupResult.SkippedDistribution:
                    _logger.LogInformation("[Issue #193] 配布版のためGPUセットアップスキップ");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[Issue #193] GPU環境セットアップがキャンセルされました");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #193] GPU環境セットアップでエラー発生 - CPUモードで続行");
            // GPU環境セットアップ失敗は致命的エラーではないので続行
        }
    }

    /// <summary>
    /// [Issue #193] GPU環境セットアップ進捗イベントハンドラ
    /// </summary>
    private void OnGpuSetupProgressChanged(object? sender, GpuSetupProgressEventArgs e)
    {
        ReportProgress("setup_gpu", e.Message, e.IsCompleted, e.Progress);
    }

    /// <summary>
    /// [Issue #185] ダウンロード進捗イベントハンドラ
    /// ダウンロード進捗をローディング画面に転送します
    /// </summary>
    private void OnDownloadProgressChanged(object? sender, ComponentDownloadProgressEventArgs e)
    {
        var progressPercent = (int)Math.Round(e.PercentComplete);
        var message = FormatDownloadMessage(e);

        ReportProgress("download_components", message, e.IsCompleted, progressPercent);
    }

    /// <summary>
    /// ダウンロード進捗メッセージをフォーマットします
    /// [Issue #198] StatusMessageが設定されている場合はそれを優先表示
    /// </summary>
    private static string FormatDownloadMessage(ComponentDownloadProgressEventArgs e)
    {
        // [Issue #198] StatusMessageがあれば優先表示（展開中などの状態表示）
        if (!string.IsNullOrEmpty(e.StatusMessage))
        {
            return e.StatusMessage;
        }

        if (e.IsCompleted)
        {
            // [Issue #198] 「ダウンロード完了」→「インストール完了」に変更
            // 解凍まで完了してから呼ばれるため、より正確な表現に
            return $"{e.Component.DisplayName} のインストール完了";
        }

        if (!string.IsNullOrEmpty(e.ErrorMessage))
        {
            return $"{e.Component.DisplayName}: {e.ErrorMessage}";
        }

        var receivedMB = e.BytesReceived / 1024.0 / 1024.0;
        var totalMB = e.TotalBytes / 1024.0 / 1024.0;
        var speedMBps = e.SpeedBytesPerSecond / 1024.0 / 1024.0;

        var etaStr = e.EstimatedTimeRemaining.HasValue
            ? $" (残り {e.EstimatedTimeRemaining.Value.TotalSeconds:F0}秒)"
            : "";

        return $"{e.Component.DisplayName}: {receivedMB:F1}MB / {totalMB:F1}MB ({speedMBps:F1}MB/s){etaStr}";
    }
}
