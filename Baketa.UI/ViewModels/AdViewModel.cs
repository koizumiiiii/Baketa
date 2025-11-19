using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// 広告表示ViewModel
/// </summary>
public sealed class AdViewModel : ViewModelBase, IDisposable
{
    private readonly IAdvertisementService _advertisementService;
    private readonly ILogger<AdViewModel> _logger;
    private bool _disposed;

    private bool _shouldShowAd;
    private string _adHtmlContent = string.Empty;

    /// <summary>
    /// 広告を表示するかどうか
    /// </summary>
    public bool ShouldShowAd
    {
        get => _shouldShowAd;
        private set => this.RaiseAndSetIfChanged(ref _shouldShowAd, value);
    }

    /// <summary>
    /// 広告HTML内容
    /// </summary>
    public string AdHtmlContent
    {
        get => _adHtmlContent;
        private set => this.RaiseAndSetIfChanged(ref _adHtmlContent, value);
    }

    public AdViewModel(
        IAdvertisementService advertisementService,
        IEventAggregator eventAggregator,
        ILogger<AdViewModel> logger) : base(eventAggregator, logger)
    {
        _advertisementService = advertisementService ?? throw new ArgumentNullException(nameof(advertisementService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 初期状態を取得
        ShouldShowAd = _advertisementService.ShouldShowAd;
        AdHtmlContent = _advertisementService.AdHtmlContent;

        // イベント購読
        _advertisementService.AdDisplayChanged += OnAdDisplayChanged;

        // 広告読み込み
        LoadAdSafely();

        _logger.LogInformation("AdViewModel初期化完了: ShouldShowAd={ShouldShowAd}", ShouldShowAd);
    }

    /// <summary>
    /// 広告を安全にロード（Fire-and-Forget用の例外ハンドリングラッパー）
    /// </summary>
    private async void LoadAdSafely()
    {
        try
        {
            await LoadAdAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "広告ロード中に予期しないエラーが発生: {Message}", ex.Message);
        }
    }

    private async Task LoadAdAsync()
    {
        try
        {
            await _advertisementService.LoadAdAsync().ConfigureAwait(false);

            // UIスレッドで更新
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AdHtmlContent = _advertisementService.AdHtmlContent;
                _logger.LogInformation("広告コンテンツをロード完了");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "広告ロード中にエラーが発生: {Message}", ex.Message);
        }
    }

    private void OnAdDisplayChanged(object? sender, AdDisplayChangedEventArgs e)
    {
        _logger.LogInformation("広告表示状態変更: {ShouldShowAd} (理由: {Reason})", e.ShouldShowAd, e.Reason);

        // UIスレッドで更新
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ShouldShowAd = e.ShouldShowAd;

            if (ShouldShowAd)
            {
                LoadAdSafely();
            }
            else
            {
                AdHtmlContent = string.Empty;
            }
        });
    }

    public new void Dispose()
    {
        if (_disposed) return;

        _advertisementService.AdDisplayChanged -= OnAdDisplayChanged;
        _disposed = true;

        base.Dispose();

        _logger.LogDebug("AdViewModel破棄完了");
    }
}
