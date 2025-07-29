using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Translation;
using Baketa.UI.Utils;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using DrawingRectangle = System.Drawing.Rectangle;

namespace Baketa.UI.Views.Overlay;

/// <summary>
/// AR風翻訳表示専用オーバーレイウィンドウ
/// 元テキストの正確な位置に翻訳テキストを重ね表示し、Google翻訳カメラのような体験を提供
/// </summary>
public partial class ARTranslationOverlayWindow : Window, IDisposable
{
    // データプロパティ
    public int ChunkId { get; init; }
    public string OriginalText { get; init; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public DrawingRectangle TargetBounds { get; init; }
    public IntPtr SourceWindowHandle { get; init; }

    #pragma warning disable CS0649 // Field is never assigned
    private readonly ILogger<ARTranslationOverlayWindow>? _logger;
    #pragma warning restore CS0649
    private bool _disposed;

    public ARTranslationOverlayWindow()
    {
        try
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🏗️ [ARTranslationOverlay] ARTranslationOverlayWindow コンストラクタ開始");
            
            // AvaloniaXamlLoaderを使用してXAMLをロード
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ [ARTranslationOverlay] AR XAML ロード完了");
            
            // ウィンドウプロパティ設定
            DataContext = this;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            SystemDecorations = SystemDecorations.None;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Background = Brushes.Transparent;
            
            // AR表示用の追加設定
            ShowActivated = false; // アクティブ化しない
            WindowStartupLocation = WindowStartupLocation.Manual; // 手動位置設定
            
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ [ARTranslationOverlay] ARウィンドウプロパティ設定完了");
            
            _logger?.LogDebug("🖼️ ARTranslationOverlayWindow created - ChunkId: {ChunkId}", ChunkId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ ARTranslationOverlayWindow constructor error: {ex.Message}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [ARTranslationOverlay] ARTranslationOverlayWindow constructor error: {ex.Message}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [ARTranslationOverlay] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// TextChunkを使用してAR風表示を行う
    /// 元テキストの正確な位置・サイズで翻訳テキストを重ね表示
    /// </summary>
    public async Task ShowAROverlayAsync(
        TextChunk textChunk, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        ThrowIfDisposed();

        if (!textChunk.CanShowAR())
        {
            _logger?.LogWarning("AR表示条件を満たしていません: {ARLog}", textChunk.ToARLogString());
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🎯 [ARTranslationOverlay] AR表示開始 - {textChunk.ToARLogString()}");
                
                // TextChunkの座標とサイズを正確に適用
                var arPosition = textChunk.GetARPosition();
                var arSize = textChunk.GetARSize();
                var arFontSize = textChunk.CalculateARFontSize();
                
                // OCRで取得した元の座標情報をデバッグ出力
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 [ARTranslationOverlay] OCR取得座標 - ChunkId: {textChunk.ChunkId}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   Combined Bounds: X={textChunk.CombinedBounds.X}, Y={textChunk.CombinedBounds.Y}, W={textChunk.CombinedBounds.Width}, H={textChunk.CombinedBounds.Height}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   Original Text: '{textChunk.CombinedText}' | Translated Text: '{textChunk.TranslatedText}'");
                
                // 詳細な座標デバッグ情報を取得
                var screen = Screens.Primary;
                var scaling = screen?.Scaling ?? 1.0;
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 [ARTranslationOverlay] 詳細座標デバッグ - ChunkId: {textChunk.ChunkId}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   スクリーンスケーリング: {scaling}");
                if (screen != null) {
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   スクリーン解像度: {screen.Bounds.Width}x{screen.Bounds.Height}");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   ワーキングエリア: {screen.WorkingArea}");
                }
                
                // 各種座標変換のテスト
                var originalX = arPosition.X;
                var originalY = arPosition.Y;
                var scaledDownX = (int)(originalX / scaling);  // スケーリングで除算
                var scaledDownY = (int)(originalY / scaling);
                var scaledUpX = (int)(originalX * scaling);    // スケーリングで乗算
                var scaledUpY = (int)(originalY * scaling);
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   OCR元座標: ({originalX}, {originalY})");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   スケールダウン: ({scaledDownX}, {scaledDownY}) [= 元座標 / {scaling}]");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   スケールアップ: ({scaledUpX}, {scaledUpY}) [= 元座標 * {scaling}]");
                
                // 3つのパターンでテスト - まずはスケールダウンを試す
                var testX = scaledDownX;
                var testY = scaledDownY;
                var testWidth = (int)(arSize.Width / scaling);
                var testHeight = (int)(arSize.Height / scaling);
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   テスト座標: ({testX}, {testY})");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   テストサイズ: ({testWidth}, {testHeight})");
                
                // スケールダウンした座標で設定
                Width = testWidth;
                Height = testHeight;
                Position = new PixelPoint(testX, testY);
                
                // 実際のウィンドウ位置を再確認
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   実際のWindow位置: X={Position.X}, Y={Position.Y}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   実際のWindowサイズ: W={Width}, H={Height}");
                
                // 実際に設定される表示座標をデバッグ出力
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"📺 [ARTranslationOverlay] AR表示座標 - ChunkId: {textChunk.ChunkId}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   Window Position: X={arPosition.X}, Y={arPosition.Y}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   Window Size: W={arSize.Width}, H={arSize.Height}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   Font Size: {arFontSize}px (Height={textChunk.CombinedBounds.Height}px * 0.45)");
                
                // DPI情報を取得して表示
                try 
                {
                    var primaryScreen = Screens.Primary;
                    if (primaryScreen != null)
                    {
                        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   Screen Scaling: {primaryScreen.Scaling} | WorkingArea: {primaryScreen.WorkingArea}");
                        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   Screen Bounds: {primaryScreen.Bounds}");
                    }
                }
                catch (Exception dpiEx)
                {
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   DPI情報取得失敗: {dpiEx.Message}");
                }
                
                // AR風スタイルを適用
                ApplyARStyle(arFontSize, textChunk.TranslatedText);
                
                // ウィンドウを表示（アクティブ化なし）
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🚦 [ARTranslationOverlay] AR Show()呼び出し前 - ChunkId: {textChunk.ChunkId}");
                Show();
                
                // ウィンドウを最前面に置くがアクティブ化はしない
                Topmost = true;
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [ARTranslationOverlay] AR Show()呼び出し完了 - ChunkId: {textChunk.ChunkId}");
                
                _logger?.LogDebug("📺 AR表示完了 - {ARLog}", textChunk.ToARLogString());
                
            }, DispatcherPriority.Normal, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ AR表示エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
            throw;
        }
    }

    /// <summary>
    /// AR風スタイルを適用
    /// 自動計算されたフォントサイズと背景色で元テキストを完全に隠す
    /// </summary>
    private void ApplyARStyle(int fontSize, string translatedText)
    {
        try
        {
            // TextBlockを取得してスタイルを適用
            var textBlock = this.FindControl<TextBlock>("ARTranslatedTextBlock");
            if (textBlock != null)
            {
                // 翻訳テキストを設定
                textBlock.Text = translatedText ?? string.Empty;
                
                // 自動計算されたフォントサイズを適用
                textBlock.FontSize = fontSize;
                
                // AR表示用のスタイル設定
                textBlock.TextWrapping = TextWrapping.NoWrap;
                textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                textBlock.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                textBlock.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [ARTranslationOverlay] ARスタイル適用完了 - FontSize: {fontSize} | Text: '{translatedText}'");
            }
            else
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "❌ [ARTranslationOverlay] ARTranslatedTextBlockが見つかりません");
            }
            
            // 背景色を自動設定（元テキスト隠蔽用）
            var border = this.FindControl<Border>("AROverlayBorder");
            if (border != null)
            {
                var backgroundColor = CalculateOptimalBackgroundColor();
                var textColor = CalculateOptimalTextColor(backgroundColor);
                
                border.Background = new SolidColorBrush(backgroundColor);
                if (textBlock != null)
                {
                    textBlock.Foreground = new SolidColorBrush(textColor);
                }
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [ARTranslationOverlay] AR色設定完了 - Background: {backgroundColor} | Text: {textColor}");
            }
        }
        catch (Exception ex)
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [ARTranslationOverlay] ARスタイル適用エラー: {ex.Message}");
            _logger?.LogError(ex, "❌ ARスタイル適用エラー - ChunkId: {ChunkId}", ChunkId);
        }
    }

    /// <summary>
    /// 元テキストを隠すための最適な背景色を計算
    /// ゲーム画面に馴染む半透明の背景色で元テキストを隠蔽
    /// </summary>
    private Color CalculateOptimalBackgroundColor()
    {
        // TODO: 将来的には元画像の背景色を解析して最適な隠蔽色を選択
        // 現在はゲーム画面に馴染む半透明の背景色を使用
        return Color.FromArgb(220, 240, 240, 240); // 半透明の白系背景色（Alpha=220/255）
    }

    /// <summary>
    /// 背景色に対して最適なテキスト色を計算
    /// </summary>
    private Color CalculateOptimalTextColor(Color backgroundColor)
    {
        // 背景色の明度に基づいてテキスト色を決定
        var brightness = (backgroundColor.R * 0.299 + backgroundColor.G * 0.587 + backgroundColor.B * 0.114) / 255.0;
        
        // 明るい背景なら黒文字、暗い背景なら白文字
        return brightness > 0.5 ? Colors.Black : Colors.White;
    }

    /// <summary>
    /// AR表示内容を更新
    /// </summary>
    public async Task UpdateARContentAsync(
        TextChunk updatedTextChunk, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!updatedTextChunk.CanShowAR())
        {
            await HideAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 新しい位置・サイズ・スタイルを適用
                var arPosition = updatedTextChunk.GetARPosition();
                var arSize = updatedTextChunk.GetARSize();
                var arFontSize = updatedTextChunk.CalculateARFontSize();
                
                Width = arSize.Width;
                Height = arSize.Height;
                Position = new PixelPoint(arPosition.X, arPosition.Y);
                
                ApplyARStyle(arFontSize, updatedTextChunk.TranslatedText);
                
                _logger?.LogDebug("🔄 AR表示更新完了 - {ARLog}", updatedTextChunk.ToARLogString());
                    
            }, DispatcherPriority.Normal, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ AR表示更新エラー - ChunkId: {ChunkId}", updatedTextChunk.ChunkId);
            throw;
        }
    }

    /// <summary>
    /// ARオーバーレイを非表示にする
    /// </summary>
    public async Task HideAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _logger?.LogDebug("🚫 AR表示非表示 - ChunkId: {ChunkId}", ChunkId);
                Hide();
            }, DispatcherPriority.Normal, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ AR表示非表示エラー - ChunkId: {ChunkId}", ChunkId);
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
            _logger?.LogDebug("🧹 ARTranslationOverlayWindow disposing - ChunkId: {ChunkId}", ChunkId);
            
            // UIスレッドでCloseを呼び出す
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Close();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "❌ AR Window close error - ChunkId: {ChunkId}", ChunkId);
                }
            });
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ AR Dispose error - ChunkId: {ChunkId}", ChunkId);
        }
    }
}