using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// 翻訳結果オーバーレイのViewModel
/// αテスト向けシンプル版 - 翻訳結果のみの表示
/// </summary>
public class TranslationResultOverlayViewModel : ViewModelBase
{
    private string _translatedText = string.Empty;
    private string _originalText = string.Empty;
    private bool _isVisible;
    private double _overlayOpacity = 0.9;
    private double _positionX = 100;
    private double _positionY = 100;
    private double _maxWidth = 400;

    public TranslationResultOverlayViewModel(
        IEventAggregator eventAggregator,
        ILogger<TranslationResultOverlayViewModel> logger)
        : base(eventAggregator, logger)
    {
        // 初期状態は非表示
        IsVisible = false;
        
        InitializeEventHandlers();
    }

    #region Properties

    /// <summary>
    /// 翻訳済みテキスト
    /// </summary>
    public string TranslatedText
    {
        get => _translatedText;
        set => this.RaiseAndSetIfChanged(ref _translatedText, value);
    }

    /// <summary>
    /// 元テキスト（デバッグ用）
    /// </summary>
    public string OriginalText
    {
        get => _originalText;
        set => this.RaiseAndSetIfChanged(ref _originalText, value);
    }

    /// <summary>
    /// オーバーレイの表示状態
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    /// <summary>
    /// オーバーレイの透明度
    /// </summary>
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set => this.RaiseAndSetIfChanged(ref _overlayOpacity, value);
    }

    /// <summary>
    /// オーバーレイのX位置
    /// </summary>
    public double PositionX
    {
        get => _positionX;
        set => this.RaiseAndSetIfChanged(ref _positionX, value);
    }

    /// <summary>
    /// オーバーレイのY位置
    /// </summary>
    public double PositionY
    {
        get => _positionY;
        set => this.RaiseAndSetIfChanged(ref _positionY, value);
    }

    /// <summary>
    /// オーバーレイの最大幅
    /// </summary>
    public double MaxWidth
    {
        get => _maxWidth;
        set => this.RaiseAndSetIfChanged(ref _maxWidth, value);
    }

    /// <summary>
    /// テキストが存在するかどうか
    /// </summary>
    public bool HasText => !string.IsNullOrWhiteSpace(TranslatedText);

    #endregion

    #region Event Handlers

    private void InitializeEventHandlers()
    {
        // 翻訳結果表示イベントを購読
        SubscribeToEvent<TranslationResultDisplayEvent>(OnTranslationResultDisplay);
        
        // 翻訳表示切り替えイベントを購読
        SubscribeToEvent<TranslationDisplayVisibilityChangedEvent>(OnTranslationDisplayVisibilityChanged);
        
        // 翻訳停止イベントを購読（表示をクリア）
        SubscribeToEvent<StopTranslationRequestEvent>(OnStopTranslationRequest);
    }

    private Task OnTranslationResultDisplay(TranslationResultDisplayEvent displayEvent)
    {
        try
        {
            // 翻訳結果を表示
            OriginalText = displayEvent.OriginalText;
            TranslatedText = displayEvent.TranslatedText;
            
            // 位置を更新（OCR検出位置ベース）
            if (displayEvent.DetectedPosition.HasValue)
            {
                var position = displayEvent.DetectedPosition.Value;
                PositionX = Math.Max(0, position.X);
                PositionY = Math.Max(0, position.Y);
            }
            
            // 翻訳が有効な場合のみ表示
            if (HasText)
            {
                IsVisible = true;
                Logger?.LogDebug("Translation result displayed: {Text}", TranslatedText);
            }
            else
            {
                IsVisible = false;
                Logger?.LogDebug("Translation result hidden: empty text");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error displaying translation result");
        }
        
        return Task.CompletedTask;
    }

    private Task OnTranslationDisplayVisibilityChanged(TranslationDisplayVisibilityChangedEvent visibilityEvent)
    {
        IsVisible = visibilityEvent.IsVisible && HasText;
        Logger?.LogDebug("Translation display visibility changed: {IsVisible}", IsVisible);
        return Task.CompletedTask;
    }

    private Task OnStopTranslationRequest(StopTranslationRequestEvent stopEvent)
    {
        // 翻訳停止時は表示をクリア
        IsVisible = false;
        TranslatedText = string.Empty;
        OriginalText = string.Empty;
        Logger?.LogDebug("Translation overlay cleared");
        return Task.CompletedTask;
    }

    #endregion
}