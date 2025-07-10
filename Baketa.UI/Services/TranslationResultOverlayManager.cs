using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework.Events;
using Baketa.UI.ViewModels;
using Baketa.UI.Views;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// 翻訳結果オーバーレイの管理サービス
/// </summary>
public class TranslationResultOverlayManager(
    IEventAggregator eventAggregator,
    ILogger<TranslationResultOverlayManager> logger) : IDisposable
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly ILogger<TranslationResultOverlayManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private TranslationResultOverlayView? _overlayWindow;
    private TranslationResultOverlayViewModel? _viewModel;
    private bool _isInitialized;
    private bool _disposed;

    /// <summary>
    /// オーバーレイを初期化
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized || _disposed)
            return;

        try
        {
            _logger.LogDebug("Initializing translation result overlay");

            // ViewModelを作成
            _viewModel = new TranslationResultOverlayViewModel(_eventAggregator, 
                Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<TranslationResultOverlayViewModel>(
                    Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance));

            // UIスレッドでウィンドウを作成
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _overlayWindow = new TranslationResultOverlayView
                {
                    DataContext = _viewModel
                };
            });

            _isInitialized = true;
            _logger.LogInformation("Translation result overlay initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize translation result overlay");
            throw;
        }
    }

    /// <summary>
    /// オーバーレイを表示
    /// </summary>
    public async Task ShowAsync()
    {
        if (!_isInitialized || _disposed)
        {
            await InitializeAsync().ConfigureAwait(false);
        }

        if (_overlayWindow != null && _viewModel != null)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _viewModel.IsVisible = true;
            });
        }
    }

    /// <summary>
    /// オーバーレイを非表示
    /// </summary>
    public async Task HideAsync()
    {
        if (_overlayWindow != null && _viewModel != null)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _viewModel.IsVisible = false;
            });
        }
    }

    /// <summary>
    /// 翻訳結果を表示
    /// </summary>
    public async Task DisplayTranslationResultAsync(string originalText, string translatedText, System.Drawing.Point? position = null)
    {
        if (!_isInitialized || _disposed)
        {
            await InitializeAsync().ConfigureAwait(false);
        }

        if (_viewModel != null)
        {
            var displayEvent = new TranslationResultDisplayEvent
            {
                OriginalText = originalText,
                TranslatedText = translatedText,
                DetectedPosition = position
            };

            await _eventAggregator.PublishAsync(displayEvent).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// オーバーレイの透明度を設定
    /// </summary>
    public void SetOpacity(double opacity)
    {
        if (_viewModel != null)
        {
            _viewModel.OverlayOpacity = Math.Max(0.1, Math.Min(1.0, opacity));
        }
    }

    /// <summary>
    /// オーバーレイの最大幅を設定
    /// </summary>
    public void SetMaxWidth(double maxWidth)
    {
        if (_viewModel != null)
        {
            _viewModel.MaxWidth = Math.Max(200, Math.Min(800, maxWidth));
        }
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _overlayWindow?.Close();
            _overlayWindow = null;
            _viewModel = null;
            _isInitialized = false;
            _disposed = true;
            
            _logger.LogDebug("Translation result overlay disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing translation result overlay");
        }
        
        GC.SuppressFinalize(this);
    }
}
