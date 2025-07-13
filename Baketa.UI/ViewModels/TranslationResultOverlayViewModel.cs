using System;
using System.Collections.ObjectModel;
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
    private string _translatedText = "デバッグ: 翻訳結果テスト"; // デバッグ用ダミーデータ
    private string _originalText = "Debug: Original Text"; // デバッグ用ダミーデータ
    private bool _isOverlayVisible;
    private double _overlayOpacity = 0.9;
    private double _positionX = 100;
    private double _positionY = 100;
    private double _maxWidth = 400;

    public TranslationResultOverlayViewModel(
        IEventAggregator eventAggregator,
        ILogger<TranslationResultOverlayViewModel> logger)
        : base(eventAggregator, logger)
    {
        var instanceId = this.GetHashCode().ToString("X8");
        Console.WriteLine($"🏗️ TranslationResultOverlayViewModel作成 - インスタンスID: {instanceId}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🏗️ TranslationResultOverlayViewModel作成 - インスタンスID: {instanceId}");
        
        // 初期状態は非表示（翻訳開始時に表示される）
        IsOverlayVisible = false;
        
        InitializeEventHandlers();
        
        Console.WriteLine($"✅ TranslationResultOverlayViewModel初期化完了 - インスタンスID: {instanceId}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ TranslationResultOverlayViewModel初期化完了 - インスタンスID: {instanceId}");
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
            try
            {
                this.RaiseAndSetIfChanged(ref _translatedText, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でTranslatedText設定失敗 - 直接設定で続行");
                _translatedText = value;
            }
        }
    }

    /// <summary>
    /// 元テキスト（デバッグ用）
    /// </summary>
    public string OriginalText
    {
        get => _originalText;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _originalText, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でOriginalText設定失敗 - 直接設定で続行");
                _originalText = value;
            }
        }
    }

    /// <summary>
    /// オーバーレイの表示状態
    /// </summary>
    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set
        {
            var instanceId = this.GetHashCode().ToString("X8");
            Console.WriteLine($"🔧 IsOverlayVisibleプロパティセッター呼び出し: {_isOverlayVisible} -> {value} (インスタンスID: {instanceId})");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔧 IsOverlayVisibleプロパティセッター呼び出し: {_isOverlayVisible} -> {value} (インスタンスID: {instanceId})");
            
            try
            {
                Console.WriteLine($"🔧 RaiseAndSetIfChangedを実行中 - 現在値: {_isOverlayVisible}, 新しい値: {value}, 値の比較: {_isOverlayVisible.Equals(value)}");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔧 RaiseAndSetIfChangedを実行中 - 現在値: {_isOverlayVisible}, 新しい値: {value}, 値の比較: {_isOverlayVisible.Equals(value)}");
                
                // RaiseAndSetIfChangedが値の変更を検出するかチェック
                var oldValue = _isOverlayVisible;
                var changed = this.RaiseAndSetIfChanged(ref _isOverlayVisible, value);
                
                Console.WriteLine($"✅ RaiseAndSetIfChanged実行完了: _isOverlayVisible = {_isOverlayVisible}, 戻り値: {changed}, 実際に変更: {oldValue != _isOverlayVisible}");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ RaiseAndSetIfChanged実行完了: _isOverlayVisible = {_isOverlayVisible}, 戻り値: {changed}, 実際に変更: {oldValue != _isOverlayVisible}");
                
                if (!changed)
                {
                    Console.WriteLine("⚠️ RaiseAndSetIfChangedが変更なしと判定 - 強制的にPropertyChangedを送信");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ RaiseAndSetIfChangedが変更なしと判定 - 強制的にPropertyChangedを送信");
                    this.RaisePropertyChanged(nameof(IsOverlayVisible));
                    Console.WriteLine("✅ 強制PropertyChanged送信完了");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ 強制PropertyChanged送信完了");
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"⚠️ RaiseAndSetIfChanged失敗: {ex.Message}");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ RaiseAndSetIfChanged失敗: {ex.Message}");
                Logger?.LogWarning(ex, "UIスレッド違反でIsOverlayVisible設定失敗 - 直接設定で続行");
                _isOverlayVisible = value;
                
                try
                {
                    Console.WriteLine("🔧 手動でPropertyChanged送信中（例外ハンドラ内）");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔧 手動でPropertyChanged送信中（例外ハンドラ内）");
                    this.RaisePropertyChanged(nameof(IsOverlayVisible));
                    Console.WriteLine("✅ 手動PropertyChanged送信完了（例外ハンドラ内）");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ 手動PropertyChanged送信完了（例外ハンドラ内）");
                }
                catch (Exception propEx)
                {
                    Console.WriteLine($"💥 手動PropertyChanged送信失敗（例外ハンドラ内）: {propEx.Message}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 手動PropertyChanged送信失敗（例外ハンドラ内）: {propEx.Message}");
                }
            }
        }
    }

    /// <summary>
    /// オーバーレイの透明度
    /// </summary>
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _overlayOpacity, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でOverlayOpacity設定失敗 - 直接設定で続行");
                _overlayOpacity = value;
            }
        }
    }

    /// <summary>
    /// オーバーレイのX位置
    /// </summary>
    public double PositionX
    {
        get => _positionX;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _positionX, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でPositionX設定失敗 - 直接設定で続行");
                _positionX = value;
            }
        }
    }

    /// <summary>
    /// オーバーレイのY位置
    /// </summary>
    public double PositionY
    {
        get => _positionY;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _positionY, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でPositionY設定失敗 - 直接設定で続行");
                _positionY = value;
            }
        }
    }

    /// <summary>
    /// オーバーレイの最大幅
    /// </summary>
    public double MaxWidth
    {
        get => _maxWidth;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _maxWidth, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でMaxWidth設定失敗 - 直接設定で続行");
                _maxWidth = value;
            }
        }
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

    private async Task OnTranslationResultDisplay(TranslationResultDisplayEvent displayEvent)
    {
        try
        {
            var displayTimer = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"🖥️ TranslationResultOverlayViewModel.OnTranslationResultDisplay呼び出し");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖥️ TranslationResultOverlayViewModel.OnTranslationResultDisplay呼び出し");
            
            Console.WriteLine($"🔍 displayEventチェック: {(displayEvent == null ? "null" : "not null")}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔍 displayEventチェック: {(displayEvent == null ? "null" : "not null")}");
            
            if (displayEvent == null)
            {
                Console.WriteLine("💥 displayEventがnullです！");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   💥 displayEventがnullです！");
                return;
            }
            
            var originalText = displayEvent.OriginalText ?? "";
            var translatedText = displayEvent.TranslatedText ?? "";
            
            Console.WriteLine($"   📖 オリジナル: '{originalText}'");
            Console.WriteLine($"   🌐 翻訳結果: '{translatedText}'");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   📖 オリジナル: '{originalText}'");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🌐 翻訳結果: '{translatedText}'");
        
            Console.WriteLine("🔄 プロパティ設定開始");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   🔄 プロパティ設定開始");
            
            // 翻訳結果を表示
            Console.WriteLine("🔄 OriginalText設定中");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   🔄 OriginalText設定中");
            OriginalText = originalText;
            
            Console.WriteLine("🔄 TranslatedText設定中");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   🔄 TranslatedText設定中");
            TranslatedText = translatedText;
            
            Console.WriteLine("🔄 位置更新開始");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   🔄 位置更新開始");
            
            // 位置を更新（OCR検出位置ベース）
            if (displayEvent.DetectedPosition.HasValue)
            {
                var position = displayEvent.DetectedPosition.Value;
                Console.WriteLine($"🔄 位置設定: X={position.X}, Y={position.Y}");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔄 位置設定: X={position.X}, Y={position.Y}");
                PositionX = Math.Max(0, position.X);
                PositionY = Math.Max(0, position.Y);
            }
            
            Console.WriteLine("🔄 HasText判定開始");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   🔄 HasText判定開始");
            
            // 翻訳が有効な場合のみ表示
            Console.WriteLine($"🔍 HasText判定: {HasText}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔍 HasText判定: {HasText}");
            
            if (HasText)
            {
                Console.WriteLine("🔄 IsOverlayVisible=true設定中");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   🔄 IsOverlayVisible=true設定中");
                
                // UIスレッドで確実にIsOverlayVisibleを設定
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Console.WriteLine($"🧵 UIスレッドでIsOverlayVisible=true設定中 (現在のスレッドID: {System.Threading.Thread.CurrentThread.ManagedThreadId})");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   🧵 UIスレッドでIsOverlayVisible=true設定中");
                    
                    Console.WriteLine($"🔍 IsOverlayVisible設定前: {_isOverlayVisible}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔍 IsOverlayVisible設定前: {_isOverlayVisible}");
                    
                    IsOverlayVisible = true;
                    
                    Console.WriteLine($"🔍 IsOverlayVisible設定後: {_isOverlayVisible}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔍 IsOverlayVisible設定後: {_isOverlayVisible}");
                    
                    // UIスレッドでプロパティ変更通知を確実に送信
                    try
                    {
                        Console.WriteLine("🔔 手動でIsOverlayVisibleプロパティ変更通知を送信中");
                        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   🔔 手動でIsOverlayVisibleプロパティ変更通知を送信中");
                        
                        // UIスレッドで確実にPropertyChangedを発火
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                Console.WriteLine($"🔔 UIスレッド内でRaisePropertyChanged実行開始 - プロパティ名: {nameof(IsOverlayVisible)}");
                                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔔 UIスレッド内でRaisePropertyChanged実行開始 - プロパティ名: {nameof(IsOverlayVisible)}");
                                
                                // PropertyChangedイベントの購読者数をチェック（複数のアプローチで）
                                try 
                                {
                                    // ReactiveUIライブラリのバージョン情報を確認
                                    var reactiveObjectType = typeof(ReactiveUI.ReactiveObject);
                                    var assembly = reactiveObjectType.Assembly;
                                    var version = assembly.GetName().Version;
                                    Console.WriteLine($"📦 ReactiveUIアセンブリバージョン: {version}");
                                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   📦 ReactiveUIアセンブリバージョン: {version}");

                                    // ReactiveObjectのフィールド構造を詳細調査
                                    var allFields = reactiveObjectType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                    Console.WriteLine($"🔍 ReactiveObjectの全フィールド: {string.Join(", ", allFields.Select(f => $"{f.Name}({f.FieldType.Name})"))}");
                                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔍 ReactiveObjectの全フィールド: {string.Join(", ", allFields.Select(f => $"{f.Name}({f.FieldType.Name})"))}");
                                    
                                    // イベント情報も調査
                                    var allEvents = reactiveObjectType.GetEvents(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                    Console.WriteLine($"🔍 ReactiveObjectの全イベント: {string.Join(", ", allEvents.Select(e => $"{e.Name}({e.EventHandlerType?.Name})"))}");
                                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔍 ReactiveObjectの全イベント: {string.Join(", ", allEvents.Select(e => $"{e.Name}({e.EventHandlerType?.Name})"))}");
                                    
                                    // アプローチ1: ReactiveObjectのPropertyChangedHandlerフィールド
                                    var propertyChangedField = reactiveObjectType.GetField("PropertyChangedHandler", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                                    if (propertyChangedField?.GetValue(this) is System.ComponentModel.PropertyChangedEventHandler handler1)
                                    {
                                        var subscriberCount1 = handler1.GetInvocationList().Length;
                                        Console.WriteLine($"🔔 PropertyChangedイベント購読者数(PropertyChangedHandler): {subscriberCount1}");
                                        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔔 PropertyChangedイベント購読者数(PropertyChangedHandler): {subscriberCount1}");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"🔔 PropertyChangedHandlerフィールドが見つからない、またはnull");
                                        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔔 PropertyChangedHandlerフィールドが見つからない、またはnull");
                                    }
                                    
                                    // アプローチ2: INotifyPropertyChangedのPropertyChangedイベント
                                    var notifyInterface = this as System.ComponentModel.INotifyPropertyChanged;
                                    var eventInfo = typeof(System.ComponentModel.INotifyPropertyChanged).GetEvent("PropertyChanged");
                                    if (eventInfo != null)
                                    {
                                        var field = this.GetType().GetField("PropertyChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                        if (field?.GetValue(this) is System.ComponentModel.PropertyChangedEventHandler handler2)
                                        {
                                            var subscriberCount2 = handler2.GetInvocationList().Length;
                                            Console.WriteLine($"🔔 PropertyChangedイベント購読者数(INotifyPropertyChanged): {subscriberCount2}");
                                            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔔 PropertyChangedイベント購読者数(INotifyPropertyChanged): {subscriberCount2}");
                                        }
                                        else
                                        {
                                            Console.WriteLine("⚠️ PropertyChangedイベントフィールドが見つからない(INotifyPropertyChanged)");
                                            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   ⚠️ PropertyChangedイベントフィールドが見つからない(INotifyPropertyChanged)");
                                        }
                                    }
                                    
                                    // アプローチ3: 基底クラスのすべてのフィールドを調査
                                    var instanceFields = this.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                    var propertyChangedFields = instanceFields.Where(f => f.FieldType == typeof(System.ComponentModel.PropertyChangedEventHandler)).ToList();
                                    Console.WriteLine($"🔍 PropertyChangedEventHandlerタイプのフィールド数: {propertyChangedFields.Count}");
                                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔍 PropertyChangedEventHandlerタイプのフィールド数: {propertyChangedFields.Count}");
                                    
                                    foreach (var field in propertyChangedFields)
                                    {
                                        if (field.GetValue(this) is System.ComponentModel.PropertyChangedEventHandler handler3)
                                        {
                                            var subscriberCount3 = handler3.GetInvocationList().Length;
                                            Console.WriteLine($"🔔 フィールド '{field.Name}' 購読者数: {subscriberCount3}");
                                            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔔 フィールド '{field.Name}' 購読者数: {subscriberCount3}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"⚠️ PropertyChangedイベント購読者チェック失敗: {ex.Message}");
                                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   ⚠️ PropertyChangedイベント購読者チェック失敗: {ex.Message}");
                                }
                                
                                this.RaisePropertyChanged(nameof(IsOverlayVisible));
                                Console.WriteLine("✅ UIスレッドでIsOverlayVisibleプロパティ変更通知送信完了");
                                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   ✅ UIスレッドでIsOverlayVisibleプロパティ変更通知送信完了");
                            }
                            catch (Exception uiPropEx)
                            {
                                Console.WriteLine($"💥 UIスレッドでのプロパティ変更通知送信失敗: {uiPropEx.Message}");
                                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   💥 UIスレッドでのプロパティ変更通知送信失敗: {uiPropEx.Message}");
                            }
                        });
                        
                        Console.WriteLine("✅ 手動プロパティ変更通知送信スケジュール完了");
                        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   ✅ 手動プロパティ変更通知送信スケジュール完了");
                    }
                    catch (Exception propEx)
                    {
                        Console.WriteLine($"💥 手動プロパティ変更通知送信失敗: {propEx.Message}");
                        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   💥 手動プロパティ変更通知送信失敗: {propEx.Message}");
                    }
                    
                    Console.WriteLine($"✅ UIスレッドでオーバーレイ表示ON: IsOverlayVisible = {IsOverlayVisible}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   ✅ UIスレッドでオーバーレイ表示ON: IsOverlayVisible = {IsOverlayVisible}");
                });
                
                Logger?.LogDebug("Translation result displayed: {Text}", TranslatedText);
            }
            else
            {
                Console.WriteLine("🔄 IsOverlayVisible=false設定中");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "   🔄 IsOverlayVisible=false設定中");
                IsOverlayVisible = false;
                Console.WriteLine($"❌ オーバーレイ表示OFF: IsOverlayVisible = {IsOverlayVisible} (テキストが空)");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   ❌ オーバーレイ表示OFF: IsOverlayVisible = {IsOverlayVisible} (テキストが空)");
                Logger?.LogDebug("Translation result hidden: empty text");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 TranslationResultOverlayViewModel.OnTranslationResultDisplay例外: {ex.GetType().Name}: {ex.Message}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   💥 TranslationResultOverlayViewModel.OnTranslationResultDisplay例外: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"💥 スタックトレース: {ex.StackTrace}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   💥 スタックトレース: {ex.StackTrace}");
            Logger?.LogError(ex, "Error displaying translation result");
        }
    }

    private Task OnTranslationDisplayVisibilityChanged(TranslationDisplayVisibilityChangedEvent visibilityEvent)
    {
        IsOverlayVisible = visibilityEvent.IsVisible && HasText;
        Logger?.LogDebug("Translation display visibility changed: {IsOverlayVisible}", IsOverlayVisible);
        return Task.CompletedTask;
    }

    private Task OnStopTranslationRequest(StopTranslationRequestEvent stopEvent)
    {
        // 翻訳停止時は表示をクリア
        IsOverlayVisible = false;
        TranslatedText = string.Empty;
        OriginalText = string.Empty;
        Logger?.LogDebug("Translation overlay cleared");
        return Task.CompletedTask;
    }

    #endregion
}