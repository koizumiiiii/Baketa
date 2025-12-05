using System.Diagnostics;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services;

/// <summary>
/// ローディング画面初期化サービス
/// ローディング画面で実行される5つの初期化ステップを管理します
/// [Issue #185] 初回起動時のコンポーネントダウンロードステップを追加
/// </summary>
public class ApplicationInitializer : ILoadingScreenInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApplicationInitializer> _logger;
    private readonly IComponentDownloader? _componentDownloader;
    private readonly Stopwatch _stopwatch = new();

    /// <inheritdoc/>
    public event EventHandler<LoadingProgressEventArgs>? ProgressChanged;

    public ApplicationInitializer(
        IServiceProvider serviceProvider,
        ILogger<ApplicationInitializer> logger,
        IComponentDownloader? componentDownloader = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _componentDownloader = componentDownloader;

        // Subscribe to download progress events
        if (_componentDownloader != null)
        {
            _componentDownloader.DownloadProgressChanged += OnDownloadProgressChanged;
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
    /// </summary>
    private async Task InitializeOcrAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OCRエンジン初期化開始");

        // TODO: Issue #170完了後、実際のOCRエンジン初期化を実装
        // var ocrEngine = _serviceProvider.GetService<IOcrEngine>();
        // if (ocrEngine != null)
        // {
        //     await ocrEngine.InitializeAsync(cancellationToken).ConfigureAwait(false);
        // }

        // 暫定的にダミー処理
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("OCRエンジン初期化完了");
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "コンポーネントダウンロードに失敗しましたが、続行します");
            // ダウンロード失敗は致命的エラーではないので続行
        }
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
