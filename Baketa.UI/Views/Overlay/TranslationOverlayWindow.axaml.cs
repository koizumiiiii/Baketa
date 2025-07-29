#pragma warning disable AVLN5001 // AR風翻訳UIへの移行中のため廃止予定警告を抑制
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.UI;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using DrawingRectangle = System.Drawing.Rectangle;

namespace Baketa.UI.Views.Overlay;

/// <summary>
/// 翻訳結果表示用オーバーレイウィンドウ
/// Phase 2-C: 座標ベース翻訳表示のための個別ウィンドウコンポーネント
/// AR風UIに置き換えられたため非推奨
/// </summary>
[Obsolete("AR風翻訳UIに置き換えられました。ARTranslationOverlayWindowを使用してください。")]
public partial class TranslationOverlayWindow : Window, IDisposable
{
    // データプロパティ
    public int ChunkId { get; init; }
    public string OriginalText { get; init; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public DrawingRectangle TargetBounds { get; init; }
    public IntPtr SourceWindowHandle { get; init; }

    // 表示制御プロパティ
    public IBrush BackgroundBrush { get; private set; } = new SolidColorBrush(Colors.Black, 0.8);
    public new IBrush BorderBrush { get; private set; } = new SolidColorBrush(Colors.Gray, 1.0);
    public IBrush TextBrush { get; private set; } = new SolidColorBrush(Colors.White, 1.0);
    public new double BorderThickness { get; private set; } = 1;
    public new double CornerRadius { get; private set; } = 4;
    public Thickness ContentPadding { get; private set; } = new(8);
    public double WindowOpacity { get; private set; } = 0.9;
    public new int FontSize { get; private set; } = 14;
    public new Avalonia.Media.FontFamily FontFamily { get; private set; } = Avalonia.Media.FontFamily.Default;
    public double LineHeight { get; private set; } = 1.2;
    public bool ShowOriginalText { get; private set; }
    
    // 影効果プロパティ
    public Avalonia.Media.Color ShadowColor { get; private set; } = Colors.Black;
    public double ShadowOffsetX { get; private set; } = 2;
    public double ShadowOffsetY { get; private set; } = 2;
    public double ShadowBlurRadius { get; private set; } = 4;

    private readonly ILogger<TranslationOverlayWindow>? _logger = null!;
    private bool _disposed;

    public TranslationOverlayWindow()
    {
        try
        {
            System.Console.WriteLine("🏗️ TranslationOverlayWindow コンストラクタ開始");
            
            // AvaloniaXamlLoaderを使用してXAMLをロード
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            
            System.Console.WriteLine("✅ XAML ロード完了");
            
            // ウィンドウプロパティ設定
            DataContext = this;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            SystemDecorations = SystemDecorations.None;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Background = Brushes.Transparent;
            
            System.Console.WriteLine("✅ ウィンドウプロパティ設定完了");
            
            _logger?.LogDebug("🖼️ TranslationOverlayWindow created - ChunkId: {ChunkId}", ChunkId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ TranslationOverlayWindow constructor error: {ex.Message}");
            System.Console.WriteLine($"❌ TranslationOverlayWindow constructor error: {ex.Message}");
            System.Console.WriteLine($"❌ Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// 指定位置にオーバーレイウィンドウを表示
    /// ユーザー要求: 対象のテキストの座標位置付近に表示
    /// </summary>
    public async Task ShowAtPositionAsync(
        DrawingPoint position, 
        DrawingSize size, 
        OverlayDisplayOptions options, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                System.Console.WriteLine($"🎯 ShowAtPositionAsync - UIThread内部処理開始");
                
                // 表示オプションを適用
                ApplyDisplayOptions(options);
                
                // ウィンドウサイズと位置を設定
                Width = Math.Max(100, size.Width);
                Height = Math.Max(30, size.Height);
                Position = new PixelPoint(position.X, position.Y);
                
                System.Console.WriteLine($"📺 オーバーレイ表示前 - ChunkId: {ChunkId} | Position: ({position.X},{position.Y}) | Size: ({size.Width},{size.Height})");
                System.Console.WriteLine($"📝 テキスト: '{TranslatedText}'");
                
                // TextBlockを直接検索してテキストを設定
                try
                {
                    var translatedTextBlock = this.FindControl<TextBlock>("TranslatedTextBlock");
                    if (translatedTextBlock != null)
                    {
                        translatedTextBlock.Text = TranslatedText ?? string.Empty;
                        System.Console.WriteLine($"✅ TextBlockにテキストを直接設定: '{TranslatedText}'");
                    }
                    else
                    {
                        System.Console.WriteLine("❌ TranslatedTextBlockが見つかりません");
                    }
                    
                    var originalTextBlock = this.FindControl<TextBlock>("OriginalTextBlock");
                    if (originalTextBlock != null && ShowOriginalText)
                    {
                        originalTextBlock.Text = OriginalText ?? string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"❌ TextBlock設定エラー: {ex.Message}");
                }
                
                _logger?.LogDebug("📺 オーバーレイ表示 - ChunkId: {ChunkId} | Position: ({X},{Y}) | Size: ({W},{H})",
                    ChunkId, position.X, position.Y, size.Width, size.Height);

                // ウィンドウを表示
                System.Console.WriteLine($"🚦 Show()呼び出し前");
                Show();
                System.Console.WriteLine($"✅ Show()呼び出し完了");
                
                // フェードイン効果（オプション）
                if (options.FadeInTimeMs > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        await FadeInAsync(options.FadeInTimeMs, cancellationToken).ConfigureAwait(false);
                    }, cancellationToken);
                }
            }, DispatcherPriority.Normal, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ オーバーレイ表示エラー - ChunkId: {ChunkId}", ChunkId);
            throw;
        }
    }

    /// <summary>
    /// オーバーレイの内容を更新
    /// </summary>
    public async Task UpdateContentAsync(
        string newTranslatedText, 
        DrawingPoint newPosition, 
        DrawingSize newSize, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // テキスト更新
                TranslatedText = newTranslatedText;
                
                // サイズ・位置更新
                Width = Math.Max(100, newSize.Width);
                Height = Math.Max(30, newSize.Height);
                Position = new PixelPoint(newPosition.X, newPosition.Y);
                
                _logger?.LogDebug("🔄 オーバーレイ更新 - ChunkId: {ChunkId} | Text: '{Text}'", 
                    ChunkId, newTranslatedText);
                    
                // データバインディング更新を通知
                NotifyPropertyChanged(nameof(TranslatedText));
            }, DispatcherPriority.Normal, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ オーバーレイ更新エラー - ChunkId: {ChunkId}", ChunkId);
            throw;
        }
    }

    /// <summary>
    /// 表示オプションを適用
    /// </summary>
    public async Task ApplyDisplayOptionsAsync(OverlayDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyDisplayOptions(options);
            });
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ ApplyDisplayOptionsエラー: {ex.Message}");
            System.Console.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
            _logger?.LogError(ex, "❌ 表示オプション適用エラー - ChunkId: {ChunkId}", ChunkId);
        }
    }

    /// <summary>
    /// Brushを安全にパースする（エラー時はフォールバック値を返す）
    /// </summary>
    private IBrush SafeParseBrush(string colorString, Color fallbackColor)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(colorString))
            {
                System.Console.WriteLine($"⚠️ 空の色文字列、フォールバック使用: {fallbackColor}");
                return new SolidColorBrush(fallbackColor, 1.0);
            }

            var parsedBrush = Brush.Parse(colorString);
            System.Console.WriteLine($"✅ 色パース成功: '{colorString}' -> {parsedBrush.GetType().Name}");
            return parsedBrush;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ 色パースエラー: '{colorString}' -> {ex.Message}");
            System.Console.WriteLine($"🔄 フォールバック色使用: {fallbackColor}");
            return new SolidColorBrush(fallbackColor, 1.0);
        }
    }

    /// <summary>
    /// オーバーレイを非表示にする
    /// </summary>
    public async Task HideAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _logger?.LogDebug("🚫 オーバーレイ非表示 - ChunkId: {ChunkId}", ChunkId);
                Hide();
            }, DispatcherPriority.Normal, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ オーバーレイ非表示エラー - ChunkId: {ChunkId}", ChunkId);
        }
    }

    /// <summary>
    /// 表示オプションを適用（同期版）
    /// </summary>
    private void ApplyDisplayOptions(OverlayDisplayOptions options)
    {
        try
        {
            // 色設定（安全なパース処理）
            System.Console.WriteLine($"🎨 背景色パース開始: '{options.BackgroundColor}'");
            BackgroundBrush = SafeParseBrush(options.BackgroundColor, Colors.Black);
            
            System.Console.WriteLine($"🎨 テキスト色パース開始: '{options.TextColor}'");  
            TextBrush = SafeParseBrush(options.TextColor, Colors.White);
            
            System.Console.WriteLine($"🎨 境界色パース開始: '{options.BorderColor}'");
            BorderBrush = SafeParseBrush(options.BorderColor, Colors.Gray);
        
        // サイズ・透明度設定
        WindowOpacity = Math.Clamp(options.Opacity, 0.1, 1.0);
        FontSize = Math.Clamp(options.FontSize, 8, 72);
        BorderThickness = Math.Clamp(options.BorderThickness, 0, 10);
        CornerRadius = Math.Clamp(options.CornerRadius, 0, 50);
        ContentPadding = new Thickness(Math.Clamp(options.Padding, 0, 50));
        
        // フォント設定
        try
        {
            FontFamily = new Avalonia.Media.FontFamily(options.FontFamily);
        }
        catch
        {
            FontFamily = Avalonia.Media.FontFamily.Default;
        }
        
        // 影効果設定
        if (options.EnableShadow)
        {
            ShadowColor = Avalonia.Media.Color.Parse(options.ShadowColor);
            ShadowOffsetX = options.ShadowOffset.X;
            ShadowOffsetY = options.ShadowOffset.Y;
            ShadowBlurRadius = Math.Clamp(options.ShadowBlurRadius, 0, 50);
        }
        
            // デバッグ表示設定
            ShowOriginalText = !string.IsNullOrEmpty(OriginalText) && options.FontSize <= 12;
            
            // プロパティ変更通知
            NotifyAllPropertiesChanged();
            
            System.Console.WriteLine("✅ ApplyDisplayOptions完了");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ ApplyDisplayOptionsエラー: {ex.Message}");
            System.Console.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
            _logger?.LogError(ex, "❌ 表示オプション適用エラー - ChunkId: {ChunkId}", ChunkId);
            
            // フォールバック設定
            BackgroundBrush = new SolidColorBrush(Colors.Black, 0.8);
            TextBrush = new SolidColorBrush(Colors.White, 1.0);
            BorderBrush = new SolidColorBrush(Colors.Gray, 1.0);
            WindowOpacity = 0.9;
            FontSize = 14;
        }
    }

    /// <summary>
    /// オーバーレイを非表示にする
    /// </summary>
    public async Task HideAsync()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Hide();
                _logger?.LogDebug("🚫 オーバーレイ非表示 - ChunkId: {ChunkId}", ChunkId);
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ オーバーレイ非表示エラー - ChunkId: {ChunkId}", ChunkId);
        }
    }

    /// <summary>
    /// フェードイン効果
    /// </summary>
    private async Task FadeInAsync(int durationMs, CancellationToken cancellationToken)
    {
        const int steps = 20;
        var stepDelay = durationMs / steps;
        var targetOpacity = WindowOpacity;
        
        for (int i = 0; i <= steps; i++)
        {
            if (cancellationToken.IsCancellationRequested) return;
            
            var currentOpacity = (double)i / steps * targetOpacity;
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WindowOpacity = currentOpacity;
                NotifyPropertyChanged(nameof(WindowOpacity));
            });
            
            await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// プロパティ変更通知
    /// </summary>
    private void NotifyPropertyChanged(string _)
    {
        // Avalonia の PropertyChanged 通知システムを使用
        // 実装は簡略化
    }

    /// <summary>
    /// すべてのプロパティ変更を通知
    /// </summary>
    private void NotifyAllPropertiesChanged()
    {
        // 主要プロパティの変更を通知
        var properties = new[]
        {
            nameof(BackgroundBrush), nameof(BorderBrush), nameof(TextBrush),
            nameof(WindowOpacity), nameof(FontSize), nameof(FontFamily),
            nameof(BorderThickness), nameof(CornerRadius), nameof(ContentPadding),
            nameof(ShadowColor), nameof(ShadowOffsetX), nameof(ShadowOffsetY), nameof(ShadowBlurRadius),
            nameof(ShowOriginalText), nameof(TranslatedText)
        };

        foreach (var property in properties)
        {
            NotifyPropertyChanged(property);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _logger?.LogDebug("🧹 TranslationOverlayWindow disposing - ChunkId: {ChunkId}", ChunkId);
            
            // UIスレッドでCloseを呼び出す
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Close();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "❌ Window close error - ChunkId: {ChunkId}", ChunkId);
                }
            });
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Dispose error - ChunkId: {ChunkId}", ChunkId);
        }
    }
}