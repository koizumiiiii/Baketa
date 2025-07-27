using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Baketa.UI.Utils;
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
    private bool _isOverlayVisible;
    private double _overlayOpacity = 0.9;
    private double _positionX = 100;
    private double _positionY = 100;
    private double _maxWidth = 400;
    private int _fontSize = 14;

    public TranslationResultOverlayViewModel(
        IEventAggregator eventAggregator,
        ILogger<TranslationResultOverlayViewModel> logger)
        : base(eventAggregator, logger)
    {
        var instanceId = this.GetHashCode().ToString("X8", CultureInfo.InvariantCulture);
        Console.WriteLine($"🏗️ TranslationResultOverlayViewModel作成 - インスタンスID: {instanceId}");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🏗️ TranslationResultOverlayViewModel作成 - インスタンスID: {instanceId}");
        
        // 初期状態は非表示（翻訳開始時に表示される）
        IsOverlayVisible = false;
        
        InitializeEventHandlers();
        
        Console.WriteLine($"✅ TranslationResultOverlayViewModel初期化完了 - インスタンスID: {instanceId}");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ TranslationResultOverlayViewModel初期化完了 - インスタンスID: {instanceId}");
    }

    #region Properties

    /// <summary>
    /// 翻訳済みテキスト
    /// </summary>
    public string TranslatedText
    {
        get => _translatedText;
        set
        {
            var changed = SetPropertySafe(ref _translatedText, value);
            if (changed)
            {
                // HasTextプロパティの変更通知も安全に送信
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    this.RaisePropertyChanged(nameof(HasText));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try
                        {
                            this.RaisePropertyChanged(nameof(HasText));
                        }
                        catch (Exception ex)
                        {
                            Logger?.LogWarning(ex, "HasTextプロパティ変更通知失敗");
                        }
                    });
                }
            }
        }
    }

    /// <summary>
    /// 元テキスト（デバッグ用）
    /// </summary>
    public string OriginalText
    {
        get => _originalText;
        set { SetPropertySafe(ref _originalText, value); }
    }

    /// <summary>
    /// オーバーレイの表示状態
    /// </summary>
    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set { SetPropertySafe(ref _isOverlayVisible, value); }
    }

    /// <summary>
    /// オーバーレイの透明度
    /// </summary>
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set { SetPropertySafe(ref _overlayOpacity, value); }
    }

    /// <summary>
    /// オーバーレイのX位置
    /// </summary>
    public double PositionX
    {
        get => _positionX;
        set { SetPropertySafe(ref _positionX, value); }
    }

    /// <summary>
    /// オーバーレイのY位置
    /// </summary>
    public double PositionY
    {
        get => _positionY;
        set { SetPropertySafe(ref _positionY, value); }
    }

    /// <summary>
    /// オーバーレイの最大幅
    /// </summary>
    public double MaxWidth
    {
        get => _maxWidth;
        set { SetPropertySafe(ref _maxWidth, value); }
    }

    /// <summary>
    /// フォントサイズ
    /// </summary>
    public int FontSize
    {
        get => _fontSize;
        set
        {
            var changed = SetPropertySafe(ref _fontSize, value);
            if (changed)
            {
                // SmallFontSizeプロパティの変更通知も安全に送信
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    this.RaisePropertyChanged(nameof(SmallFontSize));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try
                        {
                            this.RaisePropertyChanged(nameof(SmallFontSize));
                        }
                        catch (Exception ex)
                        {
                            Logger?.LogWarning(ex, "SmallFontSizeプロパティ変更通知失敗");
                        }
                    });
                }
            }
        }
    }

    /// <summary>
    /// 小さいフォントサイズ（元テキスト用）
    /// </summary>
    public int SmallFontSize => Math.Max(8, (int)(FontSize * 0.85));

    /// <summary>
    /// テキストが存在するかどうか
    /// </summary>
    public bool HasText => !string.IsNullOrWhiteSpace(TranslatedText);

    #endregion

    #region Event Handlers

    private void InitializeEventHandlers()
    {
        // TranslationResultDisplayEvent は削除 - マルチウィンドウオーバーレイシステムに移行
        
        // 翻訳表示切り替えイベントを購読
        SubscribeToEvent<TranslationDisplayVisibilityChangedEvent>(OnTranslationDisplayVisibilityChanged);
        
        // 翻訳停止イベントを購読（表示をクリア）
        SubscribeToEvent<StopTranslationRequestEvent>(OnStopTranslationRequest);
        
        // 設定変更イベントを購読（フォントサイズと透明度を更新）
        SubscribeToEvent<SettingsChangedEvent>(OnSettingsChanged);
    }

    // OnTranslationResultDisplay メソッドは削除 - マルチウィンドウオーバーレイシステムに移行

    private async Task OnTranslationDisplayVisibilityChanged(TranslationDisplayVisibilityChangedEvent visibilityEvent)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsOverlayVisible = visibilityEvent.IsVisible && HasText;
        });
        Logger?.LogDebug("Translation display visibility changed: {IsOverlayVisible}", IsOverlayVisible);
    }

    private async Task OnStopTranslationRequest(StopTranslationRequestEvent stopEvent)
    {
        // 翻訳停止時は表示をクリア（UIスレッドで実行）
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsOverlayVisible = false;
            TranslatedText = string.Empty;
            OriginalText = string.Empty;
        });
        Logger?.LogDebug("Translation overlay cleared");
    }

    private async Task OnSettingsChanged(SettingsChangedEvent settingsEvent)
    {
        try
        {
            // UI設定更新もUIスレッドで実行
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                // フォントサイズを更新
                FontSize = settingsEvent.FontSize;
                
                // 透明度を更新
                OverlayOpacity = settingsEvent.OverlayOpacity;
            });
            
            Logger?.LogDebug("Translation overlay settings updated - FontSize: {FontSize}, Opacity: {OverlayOpacity}", FontSize, OverlayOpacity);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to update translation overlay settings");
        }
    }

    #endregion
}