using System.Diagnostics;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services;

/// <summary>
/// ローディング画面初期化サービス
/// ローディング画面で実行される5つの初期化ステップを管理します
/// [Issue #185] 初回起動時のコンポーネントダウンロードステップを追加
/// [Issue #193] GPU環境自動セットアップステップを追加
/// </summary>
public class ApplicationInitializer : ILoadingScreenInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApplicationInitializer> _logger;
    private readonly IComponentDownloader? _componentDownloader;
    private readonly IGpuEnvironmentService? _gpuEnvironmentService;
    private readonly Stopwatch _stopwatch = new();

    /// <inheritdoc/>
    public event EventHandler<LoadingProgressEventArgs>? ProgressChanged;

    public ApplicationInitializer(
        IServiceProvider serviceProvider,
        ILogger<ApplicationInitializer> logger,
        IComponentDownloader? componentDownloader = null,
        IGpuEnvironmentService? gpuEnvironmentService = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _componentDownloader = componentDownloader;
        _gpuEnvironmentService = gpuEnvironmentService;

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
            // [Issue #185] Step 0: コンポーネントダウンロード（初回起動時のみ）
            await ExecuteStepAsync(
                "download_components",
                "コンポーネントをダウンロードしています...",
                DownloadMissingComponentsAsync,
                cancellationToken).ConfigureAwait(false);

            // [Issue #193] Step 0.5: GPU環境セットアップ（開発版のみ）
            await ExecuteStepAsync(
                "setup_gpu",
                "GPU環境をチェックしています...",
                SetupGpuEnvironmentAsync,
                cancellationToken).ConfigureAwait(false);

            // Step 1: 依存関係解決
            await ExecuteStepAsync(
                "resolve_dependencies",
                "依存関係を解決しています...",
                ResolveDependenciesAsync,
                cancellationToken).ConfigureAwait(false);

            // Step 2: OCRモデル読み込み
            await ExecuteStepAsync(
                "load_ocr",
                "OCRモデルを読み込んでいます...",
                InitializeOcrAsync,
                cancellationToken).ConfigureAwait(false);

            // Step 3: 翻訳エンジン初期化
            await ExecuteStepAsync(
                "init_translation",
                "翻訳エンジンを初期化しています...",
                InitializeTranslationAsync,
                cancellationToken).ConfigureAwait(false);

            // Step 4: UIコンポーネント準備
            await ExecuteStepAsync(
                "prepare_ui",
                "UIコンポーネントを準備しています...",
                PrepareUIComponentsAsync,
                cancellationToken).ConfigureAwait(false);

            _stopwatch.Stop();
            _logger.LogInformation(
                "アプリケーション初期化完了: {ElapsedMs}ms",
                _stopwatch.ElapsedMilliseconds);
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "コンポーネントダウンロードに失敗しましたが、続行します");
            // ダウンロード失敗は致命的エラーではないので続行
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
    /// </summary>
    private static string FormatDownloadMessage(ComponentDownloadProgressEventArgs e)
    {
        if (e.IsCompleted)
        {
            return $"{e.Component.DisplayName} のダウンロード完了";
        }

        if (!string.IsNullOrEmpty(e.ErrorMessage))
        {
            return $"{e.Component.DisplayName} のダウンロード失敗: {e.ErrorMessage}";
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
