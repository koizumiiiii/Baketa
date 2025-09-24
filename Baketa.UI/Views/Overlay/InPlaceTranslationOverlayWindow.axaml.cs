using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.UI.Monitors;
using Baketa.UI.Services.Monitor;
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
            
            // クリックスルー（マウスイベント透過）を有効化してゲームプレイ阻害を防止
            // Avaloniaでは直接的なクリックスルー設定はShow後に行う
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🎮 [InPlaceTranslationOverlay] クリックスルー設定はShow後に延期（ゲームプレイ阻害防止）");
            
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
                var overlayPosition = textChunk.GetBasicOverlayPosition();
                var overlaySize = textChunk.GetOverlaySize();
                var optimalFontSize = textChunk.CalculateOptimalFontSize();
                
                // ウィンドウ位置設定
                Position = new PixelPoint(overlayPosition.X, overlayPosition.Y);
                
                // ウィンドウサイズ設定
                Width = overlaySize.Width;
                Height = overlaySize.Height;
                
                // 🛡️ [CORRUPTED_TRANSLATION_FILTER] 汚染翻訳とエラーメッセージを完全フィルタリング
                if (IsCorruptedOrErrorTranslation(textChunk.TranslatedText))
                {
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🛡️ [CORRUPTED_FILTER] 汚染翻訳検出で非表示 - ChunkId: {ChunkId}, Text: '{textChunk.TranslatedText}'");
                    Hide();
                    return;
                }
                
                // インプレース表示スタイルを適用（ユーザー設定フォントサイズを使用）
                var userFontSize = GetConfiguredFontSize();
                var finalFontSize = userFontSize > 0 ? userFontSize : optimalFontSize;
                ApplyInPlaceStyle(finalFontSize, textChunk.TranslatedText);
                
                // ウィンドウを表示
                Show();
                
                // 🎯 改善されたクリックスルー設定（透明度問題対策）
                try
                {
                    var hwnd = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                    if (hwnd != IntPtr.Zero)
                    {
                        const int GWL_EXSTYLE = -20;
                        const int WS_EX_TRANSPARENT = 0x00000020;
                        const int WS_EX_LAYERED = 0x00080000;
                        const int WS_EX_TOPMOST = 0x00000008;
                        
                        var currentStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                        
                        // 🎯 クリックスルー有効化でゲームプレイ阻害を防止
                        var newStyle = currentStyle | WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TRANSPARENT;
                        var result = SetWindowLong(hwnd, GWL_EXSTYLE, newStyle);
                        
                        if (result != 0)
                        {
                            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ [InPlaceTranslationOverlay] クリックスルー有効化完了（ゲームプレイ阻害防止）");
                        }
                        else
                        {
                            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ [InPlaceTranslationOverlay] ウィンドウスタイル設定は失敗したが継続");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ [InPlaceTranslationOverlay] ウィンドウスタイル設定失敗: {ex.Message}");
                }
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [InPlaceTranslationOverlay] インプレース表示完了 - Position: ({overlayPosition.X},{overlayPosition.Y}) | Size: ({overlaySize.Width},{overlaySize.Height}) | FontSize: {finalFontSize}");
                
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
            // ユーザー設定フォントサイズをそのまま使用（強制的な最小値設定を削除）
            var effectiveFontSize = fontSize;
            
            // TextBlockを取得してスタイルを適用
            var textBlock = this.FindControl<TextBlock>("InPlaceTranslatedTextBlock");
            if (textBlock != null)
            {
                // 翻訳テキストを設定
                textBlock.Text = translatedText ?? string.Empty;
                
                // ユーザー設定フォントサイズを適用
                textBlock.FontSize = effectiveFontSize;
                
                // インプレース表示用のスタイル設定
                textBlock.TextWrapping = TextWrapping.NoWrap;
                textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                textBlock.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                textBlock.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                
                // テキスト色設定（コントラスト重視）
                textBlock.Foreground = new SolidColorBrush(Colors.Black);
                textBlock.FontWeight = FontWeight.Bold;
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [InPlaceTranslationOverlay] ユーザー設定フォント適用完了 - FontSize: {effectiveFontSize} | Text: '{translatedText}'");
            }
            else
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "❌ [InPlaceTranslationOverlay] InPlaceTranslatedTextBlockが見つかりません");
            }
            
            // Border の洗練されたデザイン適用
            var border = this.FindControl<Border>("InPlaceOverlayBorder");
            if (border != null)
            {
                // 枠無し設定
                border.CornerRadius = new CornerRadius(8); // 軽い角丸
                border.BorderThickness = new Thickness(0); // 枠無し
                
                // ブラー効果風の薄い白背景
                border.Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)); // ごく薄い白（90%透明度）
                
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ [InPlaceTranslationOverlay] 視認性向上インプレーススタイル適用完了（改善モード）");
            }
        }
        catch (Exception ex)
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [InPlaceTranslationOverlay] インプレーススタイル適用エラー: {ex.Message}");
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
                
                // 🛡️ [CORRUPTED_TRANSLATION_FILTER] 更新時も汚染翻訳とエラーメッセージを完全フィルタリング
                if (IsCorruptedOrErrorTranslation(updatedTextChunk.TranslatedText))
                {
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🛡️ [CORRUPTED_FILTER] 汚染翻訳検出で非表示 - ChunkId: {ChunkId}, Text: '{updatedTextChunk.TranslatedText}'");
                    Hide();
                    return;
                }
                
                // 新しい翻訳テキストで更新
                TranslatedText = updatedTextChunk.TranslatedText;
                
                // スタイルを再適用（ユーザー設定フォントサイズを優先）
                var userFontSize = GetConfiguredFontSize();
                var newFontSize = userFontSize > 0 ? userFontSize : updatedTextChunk.CalculateOptimalFontSize();
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

    /// <summary>
    /// 汚染翻訳またはエラーメッセージかどうかを判定
    /// Helsinki-NLP/opus-mt-en-japモデルの汚染出力を検出
    /// </summary>
    private static bool IsCorruptedOrErrorTranslation(string? translatedText)
    {
        if (string.IsNullOrEmpty(translatedText))
            return true;
            
        // 既存のエラーメッセージパターン
        if (translatedText.StartsWith("Translation Error:", StringComparison.OrdinalIgnoreCase) ||
            translatedText.StartsWith("[翻訳エラー]", StringComparison.Ordinal) ||
            translatedText.Equals("翻訳エラーが発生しました", StringComparison.Ordinal))
        {
            return true;
        }
        
        // 🚨 Helsinki-NLP/opus-mt-en-japモデル汚染パターン検出（厳格化）
        var corruptedPatterns = new[]
        {
            "オベル,",         // "Hello" -> "オベル," の正確な汚染パターン（カンマ含む）
            "テマ の 子 ら は", // "テマ の 子 ら は 奪 わ れ ず" の正確なパターン
            "ハマ テ で あ っ た", // "ハマ テ で あ っ た" の正確なパターン
            "ピハヒロテは",     // "ピハヒロテは" の正確なパターン
            "マグブキ バス",   // "マグブキ バス" の正確なパターン
        };
        
        // 汚染パターンが含まれているかチェック
        foreach (var pattern in corruptedPatterns)
        {
            if (translatedText.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }
        
        // 異常な空白文字の連続パターンを検出
        if (RepeatingSmallKanaRegex().IsMatch(translatedText))
        {
            return true; // "ぁ ぁ ぁ ぁ ぁ ぁ ぁ" のようなパターン
        }
        
        // 非日本語文字の検出（デバナーガリー文字など）
        if (DevanagariRegex().IsMatch(translatedText)) // デバナーガリー文字
        {
            return true;
        }
        
        // ランダムな数字や記号のみの文字列（短い文字列は除外）
        if (translatedText.Length > 5 && RandomNumbersAndSymbolsRegex().IsMatch(translatedText))
        {
            return true; // "2473~928" のようなパターン（5文字以上のみ）
        }
        
        return false;
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
                        Hide();
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

    // GeneratedRegex methods for performance optimization
    [GeneratedRegex(@"(ぁ|ぃ|ぅ|ぇ|ぉ)\s*\1{3,}")]
    private static partial Regex RepeatingSmallKanaRegex();

    [GeneratedRegex(@"[\u0900-\u097F]")]
    private static partial Regex DevanagariRegex();

    [GeneratedRegex(@"^[0-9\s\-\.~\p{P}]+$")]
    private static partial Regex RandomNumbersAndSymbolsRegex();
}