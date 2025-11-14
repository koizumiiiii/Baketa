using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.ErrorHandling;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models.Primitives;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// シンプル統合翻訳サービス実装（Phase 2暫定版）
/// キャプチャ→OCR→翻訳→結果返却の全プロセスを統合管理
/// ObjectDisposedException根絶のための新アーキテクチャ中核実装
/// </summary>
public sealed class SimpleTranslationService : ISimpleTranslationService, IDisposable
{
    private readonly ICaptureService _captureService;
    private readonly IImageLifecycleManager _imageManager;
    private readonly IOcrService _ocrService;
    private readonly Core.Abstractions.Services.ITranslationService _translationService;
    private readonly ISimpleErrorHandler _errorHandler;
    private readonly ILogger<SimpleTranslationService> _logger;

    private readonly BehaviorSubject<TranslationServiceStatus> _statusSubject;
    private volatile TranslationServiceStatus _currentStatus = TranslationServiceStatus.Ready;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private bool _disposed;

    /// <summary>
    /// 依存サービスを注入してSimpleTranslationServiceを初期化
    /// </summary>
    public SimpleTranslationService(
        ICaptureService captureService,
        IImageLifecycleManager imageManager,
        IOcrService ocrService,
        Core.Abstractions.Services.ITranslationService translationService,
        ISimpleErrorHandler errorHandler,
        ILogger<SimpleTranslationService> logger)
    {
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _imageManager = imageManager ?? throw new ArgumentNullException(nameof(imageManager));
        _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _statusSubject = new BehaviorSubject<TranslationServiceStatus>(_currentStatus);
        _cancellationTokenSource = new CancellationTokenSource();

        _logger.LogInformation("SimpleTranslationService initialized successfully");
    }

    /// <summary>
    /// 現在の処理状態を取得
    /// </summary>
    public TranslationServiceStatus Status => _currentStatus;

    /// <summary>
    /// 状態変化を通知するReactiveプロパティ
    /// ReactiveUIとの連携に使用
    /// </summary>
    public IObservable<TranslationServiceStatus> StatusChanges => _statusSubject.AsObservable();

    /// <summary>
    /// ウィンドウ情報を基に統合翻訳処理を実行（Phase 2暫定実装）
    /// キャプチャ→OCR→翻訳→結果返却の全処理を包含
    /// </summary>
    public async Task<SimpleTranslationResult> ProcessTranslationAsync(
        WindowInfo windowInfo,
        CancellationToken cancellationToken = default)
    {
        if (windowInfo == null)
            throw new ArgumentNullException(nameof(windowInfo));

        ObjectDisposedException.ThrowIf(_disposed, this);

        var startTime = DateTime.UtcNow;
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _cancellationTokenSource.Token);

        UpdateStatus(TranslationServiceStatus.Processing);

        try
        {
            _logger.LogDebug("Starting translation process for window: {WindowTitle}", windowInfo.WindowTitle);

            // Phase 2暫定実装：スタブ処理
            await Task.Delay(100, combinedCts.Token);

            var totalTime = DateTime.UtcNow - startTime;

            var result = new SimpleTranslationResult
            {
                Success = true,
                TranslatedText = "Phase 2 Translation Result (Stub)",
                TextRegions = [],
                ProcessingTime = totalTime
            };

            _logger.LogInformation("Translation process completed successfully in {TotalTime}ms",
                totalTime.TotalMilliseconds);

            UpdateStatus(TranslationServiceStatus.Ready);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Translation process was cancelled");
            UpdateStatus(TranslationServiceStatus.Ready);

            return new SimpleTranslationResult
            {
                Success = false,
                ErrorMessage = "Translation process was cancelled",
                ProcessingTime = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation process failed: {ErrorMessage}", ex.Message);

            var errorInfo = new SimpleError
            {
                Operation = "ProcessTranslationAsync",
                Level = ErrorLevel.Error,
                Exception = ex,
                Context = $"Window: {windowInfo.WindowTitle}"
            };

            var recovered = await _errorHandler.HandleErrorAsync(errorInfo);

            UpdateStatus(recovered ? TranslationServiceStatus.Ready : TranslationServiceStatus.Error);

            return new SimpleTranslationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTime = DateTime.UtcNow - startTime
            };
        }
    }

    /// <summary>
    /// サービスの停止処理
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping SimpleTranslationService");

        UpdateStatus(TranslationServiceStatus.Stopped);

        _cancellationTokenSource.Cancel();

        // 進行中の処理が完了するまで少し待機
        await Task.Delay(100, CancellationToken.None);

        _logger.LogInformation("SimpleTranslationService stopped successfully");
    }

    private void UpdateStatus(TranslationServiceStatus newStatus)
    {
        if (_currentStatus != newStatus)
        {
            _currentStatus = newStatus;
            _statusSubject.OnNext(newStatus);
            _logger.LogDebug("Status changed to: {Status}", newStatus);
        }
    }

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogDebug("Disposing SimpleTranslationService");

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _statusSubject?.OnCompleted();
        _statusSubject?.Dispose();

        _disposed = true;
    }

    #endregion
}
