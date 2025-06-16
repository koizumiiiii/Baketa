using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Overlay.Positioning;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Overlay.Positioning;

/// <summary>
/// Avalonia UIベースのテキスト測定サービス実装
/// </summary>
public sealed class AvaloniaTextMeasurementService : ITextMeasurementService, IDisposable
{
    private readonly ILogger<AvaloniaTextMeasurementService> _logger;
    private readonly SemaphoreSlim _measurementSemaphore = new(1, 1);
    private bool _disposed;
    
    /// <summary>
    /// 新しいAvaloniaTextMeasurementServiceを初期化します
    /// </summary>
    /// <param name="logger">ロガー</param>
    public AvaloniaTextMeasurementService(ILogger<AvaloniaTextMeasurementService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <inheritdoc/>
    public async Task<TextMeasurementResult> MeasureTextAsync(string text, TextMeasurementOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        
        await _measurementSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => MeasureTextInternal(text, options), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _measurementSemaphore.Release();
        }
    }
    
    /// <summary>
    /// 内部テキスト測定実装
    /// </summary>
    private TextMeasurementResult MeasureTextInternal(string text, TextMeasurementOptions options)
    {
        try
        {
            // フォント設定
            var fontFamily = ResolveFontFamily(options.FontFamily);
            var fontSize = options.FontSize;
            var fontWeight = ResolveFontWeight(options.FontWeight);
            
            // テキストブロック作成
            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = fontFamily,
                FontSize = fontSize,
                FontWeight = fontWeight,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = options.MaxWidth,
                Padding = new Thickness(
                    options.Padding.Left,
                    options.Padding.Top,
                    options.Padding.Right,
                    options.Padding.Bottom
                )
            };
            
            // レイアウト測定
            var availableSize = new Avalonia.Size(options.MaxWidth, double.PositiveInfinity);
            textBlock.Measure(availableSize);
            
            var measuredSize = textBlock.DesiredSize;
            var lineCount = CountLines(text, options.MaxWidth, fontSize, fontFamily);
            
            var result = new TextMeasurementResult(
                Size: new CoreSize(measuredSize.Width, measuredSize.Height),
                LineCount: lineCount,
                ActualFontSize: fontSize,
                MeasuredWith: options
            );
            
            _logger.LogDebug("テキスト測定完了: {Text} → {Size}, {LineCount}行",
                text.Length > 50 ? text[..50] + "..." : text,
                result.Size,
                result.LineCount);
            
            return result;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "テキスト測定中に引数エラーが発生しました: {Text}", text);
            return CreateFallbackMeasurement(text, options);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "テキスト測定中に無効な操作エラーが発生しました: {Text}", text);
            return CreateFallbackMeasurement(text, options);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "テキスト測定中にサポートされていない操作エラーが発生しました: {Text}", text);
            return CreateFallbackMeasurement(text, options);
        }
    }
    
    /// <summary>
    /// フォントファミリーを解決します
    /// </summary>
    private FontFamily ResolveFontFamily(string fontFamilyName)
    {
        try
        {
            return new FontFamily(fontFamilyName);
        }
        catch (ArgumentException)
        {
            // 無効なフォント名の場合のフォールバック
            return FontFamily.Default;
        }
        catch (NotSupportedException)
        {
            // サポートされていないフォントの場合のフォールバック
            return FontFamily.Default;
        }
    }
    
    /// <summary>
    /// フォントウェイトを解決します
    /// </summary>
    private FontWeight ResolveFontWeight(string fontWeight)
    {
        return fontWeight.ToUpperInvariant() switch
        {
            "THIN" => FontWeight.Thin,
            "EXTRALIGHT" => FontWeight.ExtraLight,
            "LIGHT" => FontWeight.Light,
            "NORMAL" => FontWeight.Normal,
            "MEDIUM" => FontWeight.Medium,
            "SEMIBOLD" => FontWeight.SemiBold,
            "BOLD" => FontWeight.Bold,
            "EXTRABOLD" => FontWeight.ExtraBold,
            "BLACK" => FontWeight.Black,
            "EXTRABLACK" => FontWeight.ExtraBlack,
            _ => FontWeight.Normal
        };
    }
    
    /// <summary>
    /// 行数をカウントします
    /// </summary>
    private int CountLines(string text, double maxWidth, double fontSize, FontFamily fontFamily)
    {
        try
        {
            // 簡易的な行数計算
            // より正確な計算が必要な場合はFormattedTextを使用
            var lines = text.Split(['\r', '\n'], StringSplitOptions.None);
            var totalLines = 0;
            
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    totalLines++;
                    continue;
                }
                
                // 概算文字幅を使用した簡易計算
                var avgCharWidth = fontSize * 0.6; // 日本語混在での概算
                var charactersPerLine = (int)(maxWidth / avgCharWidth);
                
                if (charactersPerLine <= 0)
                    charactersPerLine = 1;
                
                var lineCount = Math.Max(1, (int)Math.Ceiling((double)line.Length / charactersPerLine));
                totalLines += lineCount;
            }
            
            return Math.Max(1, totalLines);
        }
        catch (ArgumentException)
        {
            // 文字列処理エラーのフォールバック
            return Math.Max(1, text.Split(['\r', '\n'], StringSplitOptions.None).Length);
        }
        catch (OverflowException)
        {
            // 数値オーバーフローのフォールバック
            return Math.Max(1, text.Split(['\r', '\n'], StringSplitOptions.None).Length);
        }
    }
    
    /// <summary>
    /// フォールバック測定を作成します
    /// </summary>
    private TextMeasurementResult CreateFallbackMeasurement(string text, TextMeasurementOptions options)
    {
        // 概算計算
        var avgCharWidth = options.FontSize * 0.6;
        var lineHeight = options.FontSize * 1.2;
        
        var lines = text.Split(['\r', '\n'], StringSplitOptions.None);
        var lineCount = Math.Max(1, lines.Length);
        
        var maxLineLength = lines.Length > 0 ? lines.Max(l => l.Length) : text.Length;
        var estimatedWidth = Math.Min(maxLineLength * avgCharWidth, options.MaxWidth);
        var estimatedHeight = lineCount * lineHeight;
        
        // パディング追加
        estimatedWidth += options.Padding.Horizontal;
        estimatedHeight += options.Padding.Vertical;
        
        return new TextMeasurementResult(
            Size: new CoreSize(estimatedWidth, estimatedHeight),
            LineCount: lineCount,
            ActualFontSize: options.FontSize,
            MeasuredWith: options
        );
    }
    
    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _measurementSemaphore.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// テキスト測定サービスのファクトリー
/// </summary>
public static class TextMeasurementServiceFactory
{
    /// <summary>
    /// プラットフォーム固有の測定サービスを作成します
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <returns>テキスト測定サービス</returns>
    public static ITextMeasurementService Create(ILogger<AvaloniaTextMeasurementService> logger)
    {
        return new AvaloniaTextMeasurementService(logger);
    }
}

/// <summary>
/// 測定オプションの拡張メソッド
/// </summary>
public static class TextMeasurementOptionsExtensions
{
    /// <summary>
    /// デフォルト日本語フォント設定を取得します
    /// </summary>
    public static TextMeasurementOptions ForJapanese(this TextMeasurementOptions options)
    {
        return options with
        {
            FontFamily = "Yu Gothic UI",
            FontSize = 16,
            FontWeight = "Normal"
        };
    }
    
    /// <summary>
    /// デフォルト英語フォント設定を取得します
    /// </summary>
    public static TextMeasurementOptions ForEnglish(this TextMeasurementOptions options)
    {
        return options with
        {
            FontFamily = "Segoe UI",
            FontSize = 14,
            FontWeight = "Normal"
        };
    }
    
    /// <summary>
    /// 大きめの表示用設定を取得します
    /// </summary>
    public static TextMeasurementOptions ForLargeDisplay(this TextMeasurementOptions options)
    {
        return options with
        {
            FontSize = options.FontSize * 1.25,
            Padding = new CoreThickness(15)
        };
    }
    
    /// <summary>
    /// コンパクト表示用設定を取得します
    /// </summary>
    public static TextMeasurementOptions ForCompactDisplay(this TextMeasurementOptions options)
    {
        return options with
        {
            FontSize = options.FontSize * 0.9,
            Padding = new CoreThickness(8)
        };
    }
}
