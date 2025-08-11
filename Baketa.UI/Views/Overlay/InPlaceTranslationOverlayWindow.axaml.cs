using System;
using System.Runtime.InteropServices;
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
/// インプレース翻訳表示専用オーバーレイウィンドウ
/// 元テキストの正確な位置に翻訳テキストを重ね表示し、Google翻訳カメラのような体験を提供
/// </summary>
public partial class InPlaceTranslationOverlayWindow : Window, IDisposable
{
    // Windows API for click-through
#pragma warning disable SYSLIB1054 // Use LibraryImportAttribute instead of DllImportAttribute to generate P/Invoke marshalling code at compile time
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
#pragma warning restore SYSLIB1054

    // データプロパティ
    public int ChunkId { get; init; }
    public string OriginalText { get; init; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public DrawingRectangle TargetBounds { get; init; }
    public IntPtr SourceWindowHandle { get; init; }

    #pragma warning disable CS0649 // Field is never assigned
    private readonly ILogger<InPlaceTranslationOverlayWindow>? _logger;
    #pragma warning restore CS0649
    private bool _disposed;
    
    // フォントサイズ設定（デフォルト値）
    private static int _globalFontSize = 14;

    public InPlaceTranslationOverlayWindow()
    {
        try
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🏗️ [InPlaceTranslationOverlay] InPlaceTranslationOverlayWindow コンストラクタ開始");
            
            // AvaloniaXamlLoaderを使用してXAMLをロード
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ [InPlaceTranslationOverlay] InPlace XAML ロード完了");
            
            // ウィンドウプロパティ設定
            DataContext = this;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            SystemDecorations = SystemDecorations.None;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Background = Brushes.Transparent;
            
            // インプレース表示用の追加設定
            ShowActivated = false; // アクティブ化しない
            WindowStartupLocation = WindowStartupLocation.Manual; // 手動位置設定
            
            // クリックスルー（マウスイベント透過）を有効化
            // Avaloniaでは直接的なクリックスルー設定はShow後に行う
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ [InPlaceTranslationOverlay] クリックスルー設定はShow後に延期");
            
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ [InPlaceTranslationOverlay] InPlaceウィンドウプロパティ設定完了");
            
            _logger?.LogDebug("🖼️ InPlaceTranslationOverlayWindow created - ChunkId: {ChunkId}", ChunkId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ InPlaceTranslationOverlayWindow constructor error: {ex.Message}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [InPlaceTranslationOverlay] InPlaceTranslationOverlayWindow constructor error: {ex.Message}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [InPlaceTranslationOverlay] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// TextChunkを使用してインプレース表示を行う
    /// 元テキストの正確な位置・サイズで翻訳テキストを重ね表示
    /// </summary>
    public async Task ShowInPlaceOverlayAsync(
        TextChunk textChunk, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        ThrowIfDisposed();

        if (!textChunk.CanShowInPlace())
        {
            _logger?.LogWarning("インプレース表示条件を満たしていません: {InPlaceLog}", textChunk.ToInPlaceLogString());
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🎯 [InPlaceTranslationOverlay] インプレース表示開始 - {textChunk.ToInPlaceLogString()}");
                
                // TextChunkの座標とサイズを正確に適用
                var overlayPosition = textChunk.GetOverlayPosition();
                var overlaySize = textChunk.GetOverlaySize();
                var optimalFontSize = textChunk.CalculateOptimalFontSize();
                
                // ウィンドウ位置設定
                Position = new PixelPoint(overlayPosition.X, overlayPosition.Y);
                
                // ウィンドウサイズ設定
                Width = overlaySize.Width;
                Height = overlaySize.Height;
                
                // インプレース表示スタイルを適用（設定画面のフォントサイズを使用）
                var configuredFontSize = GetConfiguredFontSize();
                var finalFontSize = configuredFontSize > 0 ? configuredFontSize : optimalFontSize;
                ApplyInPlaceStyle(finalFontSize, textChunk.TranslatedText);
                
                // ウィンドウを表示
                Show();
                
                // Show後にクリックスルー設定を適用
                try
                {
                    var hwnd = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                    if (hwnd != IntPtr.Zero)
                    {
                        // WS_EX_TRANSPARENT スタイルを追加してクリックスルーを有効化
                        const int GWL_EXSTYLE = -20;
                        const int WS_EX_TRANSPARENT = 0x00000020;
                        const int WS_EX_LAYERED = 0x00080000;
                        
                        var currentStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                        var result = SetWindowLong(hwnd, GWL_EXSTYLE, currentStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                        
                        if (result != 0)
                        {
                            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ [InPlaceTranslationOverlay] クリックスルー設定完了");
                        }
                        else
                        {
                            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ [InPlaceTranslationOverlay] クリックスルー設定は失敗したが継続");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ [InPlaceTranslationOverlay] クリックスルー設定失敗: {ex.Message}");
                }
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [InPlaceTranslationOverlay] インプレース表示完了 - Position: ({overlayPosition.X},{overlayPosition.Y}) | Size: ({overlaySize.Width},{overlaySize.Height}) | FontSize: {optimalFontSize}");
                
            }, DispatcherPriority.Normal, cancellationToken);

            _logger?.LogDebug("🎯 インプレース表示完了 - ChunkId: {ChunkId}", ChunkId);
        }
        catch (Exception ex)
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [InPlaceTranslationOverlay] インプレース表示エラー: {ex.Message}");
            _logger?.LogError(ex, "❌ インプレース表示エラー - ChunkId: {ChunkId}", ChunkId);
            throw;
        }
    }

    /// <summary>
    /// すりガラス風インプレース表示スタイルを適用
    /// 自動計算されたフォントサイズとすりガラス風背景で元テキストを美しく隠す
    /// </summary>
    /// <summary>
    /// 設定画面で設定されたフォントサイズを取得
    /// </summary>
    private static int GetConfiguredFontSize()
    {
        return _globalFontSize;
    }
    
    /// <summary>
    /// 全オーバーレイウィンドウのフォントサイズを更新（静的メソッド）
    /// </summary>
    public static void SetGlobalFontSize(int fontSize)
    {
        if (fontSize > 0 && fontSize <= 72) // 有効範囲チェック
        {
            _globalFontSize = fontSize;
        }
    }

    private void ApplyInPlaceStyle(int fontSize, string translatedText)
    {
        try
        {
            // TextBlockを取得してスタイルを適用
            var textBlock = this.FindControl<TextBlock>("InPlaceTranslatedTextBlock");
            if (textBlock != null)
            {
                // 翻訳テキストを設定
                textBlock.Text = translatedText ?? string.Empty;
                
                // 自動計算されたフォントサイズを適用
                textBlock.FontSize = fontSize;
                
                // インプレース表示用のスタイル設定
                textBlock.TextWrapping = TextWrapping.NoWrap;
                textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                textBlock.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                textBlock.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [InPlaceTranslationOverlay] すりガラス風インプレーススタイル適用完了 - FontSize: {fontSize} | Text: '{translatedText}'");
            }
            else
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "❌ [InPlaceTranslationOverlay] InPlaceTranslatedTextBlockが見つかりません");
            }
            
            // Borderのスタイル設定（XAMLのLinearGradientBrushを保持）
            var border = this.FindControl<Border>("InPlaceOverlayBorder");
            if (border != null)
            {
                // 角丸無効化の試み（FluentThemeに上書きされる）
                border.CornerRadius = new CornerRadius(0);
                
                // XAMLで設定したLinearGradientBrushはそのまま使用（上書きしない）
                // TextBlockの色のみ調整
                if (textBlock != null)
                {
                    // 読みやすいダークグレー色を設定
                    textBlock.Foreground = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
                }
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ [InPlaceTranslationOverlay] 角丸なし・すりガラス風インプレーススタイル適用完了（強制モード）");
            }
        }
        catch (Exception ex)
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [InPlaceTranslationOverlay] すりガラス風インプレーススタイル適用エラー: {ex.Message}");
            _logger?.LogError(ex, "❌ インプレーススタイル適用エラー - ChunkId: {ChunkId}", ChunkId);
        }
    }


    /// <summary>
    /// インプレース表示内容を更新
    /// </summary>
    public async Task UpdateInPlaceContentAsync(
        TextChunk updatedTextChunk, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 [InPlaceTranslationOverlay] インプレース内容更新開始 - ChunkId: {ChunkId}");
                
                // 新しい翻訳テキストで更新
                TranslatedText = updatedTextChunk.TranslatedText;
                
                // スタイルを再適用
                var newFontSize = updatedTextChunk.CalculateOptimalFontSize();
                ApplyInPlaceStyle(newFontSize, TranslatedText);
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [InPlaceTranslationOverlay] インプレース内容更新完了 - ChunkId: {ChunkId}");
                
            }, DispatcherPriority.Normal, cancellationToken);
            
            _logger?.LogDebug("🔄 インプレース内容更新完了 - ChunkId: {ChunkId}", ChunkId);
        }
        catch (Exception ex)
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [InPlaceTranslationOverlay] インプレース内容更新エラー: {ex.Message}");
            _logger?.LogError(ex, "❌ インプレース内容更新エラー - ChunkId: {ChunkId}", ChunkId);
            throw;
        }
    }

    /// <summary>
    /// オーバーレイウィンドウを非表示
    /// </summary>
    public async Task HideAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🚫 [InPlaceTranslationOverlay] インプレース非表示開始 - ChunkId: {ChunkId}");
                
                Hide();
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [InPlaceTranslationOverlay] インプレース非表示完了 - ChunkId: {ChunkId}");
                
            }, DispatcherPriority.Normal, cancellationToken);
            
            _logger?.LogDebug("🚫 インプレース非表示完了 - ChunkId: {ChunkId}", ChunkId);
        }
        catch (Exception ex)
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [InPlaceTranslationOverlay] インプレース非表示エラー: {ex.Message}");
            _logger?.LogError(ex, "❌ インプレース非表示エラー - ChunkId: {ChunkId}", ChunkId);
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
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🧹 [InPlaceTranslationOverlay] InPlaceTranslationOverlayWindow Dispose開始 - ChunkId: {ChunkId}");
            
            _disposed = true;
            
            // UIスレッドでウィンドウを閉じる
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        Close();
                    }
                    catch (Exception ex)
                    {
                        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [InPlaceTranslationOverlay] ウィンドウクローズエラー: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [InPlaceTranslationOverlay] Dispatcher.UIThread.Postエラー: {ex.Message}");
            }
            
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [InPlaceTranslationOverlay] InPlaceTranslationOverlayWindow Dispose完了 - ChunkId: {ChunkId}");
            
            _logger?.LogDebug("🧹 InPlaceTranslationOverlayWindow disposed - ChunkId: {ChunkId}", ChunkId);
        }
        catch (Exception ex)
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [InPlaceTranslationOverlay] Dispose例外: {ex.Message}");
            _logger?.LogError(ex, "❌ InPlaceTranslationOverlayWindow Dispose例外 - ChunkId: {ChunkId}", ChunkId);
        }
        
        GC.SuppressFinalize(this);
    }
}