using System.Drawing;
using System.IO;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;
// 🔥 [PHASE_K-29-G] CaptureOptions統合: TextDetectionConfig取得用
using Baketa.Core.Models.Capture;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;
using Rectangle = System.Drawing.Rectangle;

namespace Baketa.Infrastructure.Platform.Adapters;

/// <summary>
/// ITextRegionDetector インターフェース競合問題の解決アダプター
///
/// 問題: OcrExecutionStageStrategy が Capture.ITextRegionDetector を要求するが、
///       DI には OCR.TextDetection.ITextRegionDetector のみ登録されている
///
/// 解決: AdaptiveTextRegionDetector(OCR.TextDetection版) を
///       Capture.ITextRegionDetector として利用可能にするアダプター
///
/// UltraThink Phase 77.4 実装: ROI検出全画面フォールバック問題の根本解決
/// </summary>
public sealed class TextRegionDetectorAdapter : Baketa.Core.Abstractions.Capture.ITextRegionDetector
{
    private readonly Baketa.Core.Abstractions.OCR.TextDetection.ITextRegionDetector _adaptiveDetector;
    private readonly ILogger<TextRegionDetectorAdapter> _logger;
    private TextDetectionConfig _currentConfig;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="adaptiveDetector">委譲先の AdaptiveTextRegionDetector</param>
    /// <param name="logger">ログ出力用</param>
    public TextRegionDetectorAdapter(
        Baketa.Core.Abstractions.OCR.TextDetection.ITextRegionDetector adaptiveDetector,
        ILogger<TextRegionDetectorAdapter> logger)
    {
        _adaptiveDetector = adaptiveDetector ?? throw new ArgumentNullException(nameof(adaptiveDetector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // デフォルト設定で初期化
        _currentConfig = new TextDetectionConfig();
    }

    /// <summary>
    /// テキスト領域検出の実装
    /// Capture.ITextRegionDetector → OCR.TextDetection.ITextRegionDetector への変換
    /// </summary>
    /// <param name="image">入力画像 (IWindowsImage)</param>
    /// <returns>検出されたテキスト領域の矩形リスト</returns>
    public async Task<IList<Rectangle>> DetectTextRegionsAsync(IWindowsImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        try
        {
            // 🔥 [PHASE_K-29-E-1] 入力画像サイズログ追加 - 座標ズレ仮説検証
            _logger.LogDebug("🔗 [K-29-E-1] TextRegionDetectorAdapter: IWindowsImage → IAdvancedImage 変換開始 - 入力サイズ: {Width}x{Height}",
                image.Width, image.Height);

            // 1. IWindowsImage → IAdvancedImage 変換
            var advancedImage = await ConvertToAdvancedImageAsync(image).ConfigureAwait(false);

            // 🔥 [PHASE_K-29-E-1] 変換後画像サイズログ追加
            _logger.LogDebug("✅ [K-29-E-1] TextRegionDetectorAdapter: IAdvancedImage変換完了 - 変換後サイズ: {Width}x{Height}",
                advancedImage.Width, advancedImage.Height);

            try
            {
                _logger.LogDebug("📍 TextRegionDetectorAdapter: AdaptiveTextRegionDetector.DetectRegionsAsync 呼び出し");

                // 2. AdaptiveTextRegionDetector による高精度Sobel+LBP検出
                var ocrRegions = await _adaptiveDetector.DetectRegionsAsync(advancedImage).ConfigureAwait(false);

                _logger.LogInformation("✅ TextRegionDetectorAdapter: OCR領域検出完了 - {RegionCount}個の領域を検出", ocrRegions.Count);

                // 3. OCRTextRegion → Rectangle 変換
                var rectangles = ocrRegions.Select(region => region.Bounds).ToList();

                _logger.LogDebug("🎯 TextRegionDetectorAdapter: Rectangle変換完了 - {RectangleCount}個の矩形に変換", rectangles.Count);

                return rectangles;
            }
            finally
            {
                // IAdvancedImageがIDisposableを実装している場合はリソース解放
                if (advancedImage is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            // 🔥 [PHASE13.2.31K-26] Gemini推奨修正: フルスクリーンフォールバックの廃止
            // 問題: フルスクリーン領域(3840x2160)を返すとPaddlePredictor(Detector)が失敗
            // 解決策: 空のリストを返してOCR処理をスキップ、システム安定化を優先
            _logger.LogError(ex, "❌ [K-26] TextRegionDetectorAdapter: テキスト領域検出失敗 - 詳細エラー情報とスタックトレース");

            // エラー時のフォールバック: 空のリストを返す（翻訳スキップ、システム安定化）
            _logger.LogWarning("🔄 [K-26] TextRegionDetectorAdapter: フォールバック - 空のリストを返します（翻訳スキップ、PaddlePredictor過負荷回避）");
            return [];
        }
    }

    /// <summary>
    /// 検出パラメータを調整
    /// </summary>
    /// <param name="config">テキスト検出設定</param>
    public void ConfigureDetection(TextDetectionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _currentConfig = config;
        _logger.LogDebug("🔧 TextRegionDetectorAdapter: 検出設定を更新しました");

        // AdaptiveTextRegionDetectorがパラメータ設定をサポートしている場合はここで委譲
        // 現在の実装では設定をローカルに保存するのみ
        try
        {
            _adaptiveDetector.SetParameter("MinTextWidth", config.MinTextWidth);
            _adaptiveDetector.SetParameter("MinTextHeight", config.MinTextHeight);
            _adaptiveDetector.SetParameter("MinTextArea", config.MinTextArea);
            _logger.LogDebug("✅ TextRegionDetectorAdapter: AdaptiveTextRegionDetector にパラメータを委譲しました");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ TextRegionDetectorAdapter: AdaptiveTextRegionDetector パラメータ設定失敗（設定はローカル保存されました）");
        }
    }

    /// <summary>
    /// 現在の検出設定を取得
    /// </summary>
    /// <returns>現在のテキスト検出設定</returns>
    public TextDetectionConfig GetCurrentConfig()
    {
        return _currentConfig;
    }

    /// <summary>
    /// 検出精度を向上させるためのプレビューモード
    /// </summary>
    /// <param name="image">入力画像</param>
    /// <param name="showDebugInfo">デバッグ情報表示フラグ</param>
    /// <returns>検出されたテキスト領域の矩形リスト</returns>
    public async Task<IList<Rectangle>> DetectWithPreviewAsync(IWindowsImage image, bool showDebugInfo = false)
    {
        ArgumentNullException.ThrowIfNull(image);

        _logger.LogDebug("🔍 TextRegionDetectorAdapter: プレビューモード開始 (DebugInfo: {ShowDebugInfo})", showDebugInfo);

        try
        {
            // プレビューモードでは通常の検出と同様の処理を実行
            // 将来的にはデバッグ情報の表示やより詳細な解析を追加可能
            var result = await DetectTextRegionsAsync(image).ConfigureAwait(false);

            if (showDebugInfo)
            {
                _logger.LogInformation("🔍 TextRegionDetectorAdapter プレビュー結果: {RegionCount}個の領域を検出", result.Count);
                for (int i = 0; i < result.Count; i++)
                {
                    var rect = result[i];
                    _logger.LogDebug("   領域 {Index}: ({X}, {Y}) - {Width}x{Height}", i + 1, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ TextRegionDetectorAdapter: プレビューモード実行中にエラーが発生: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// IWindowsImage を IAdvancedImage に変換
    ///
    /// 実装ノート: 現時点では直接的な変換ファクトリーが存在しないため、
    /// Bitmap経由での変換を実装。将来的にはより効率的な変換方法に置き換え可能。
    /// </summary>
    /// <param name="windowsImage">変換元のWindowsImage</param>
    /// <returns>変換されたAdvancedImage</returns>
    // 🔥 [PHASE5.2] GetBitmapAsync使用によりスレッドブロッキング解消
    private async Task<IAdvancedImage> ConvertToAdvancedImageAsync(IWindowsImage windowsImage, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("🔧 [PHASE5.2] IWindowsImage → IAdvancedImage async変換開始");

        try
        {
            // 🔥 [PHASE5.2] WindowsImage から Bitmap を非同期取得（.Result削除）
            using var bitmap = await windowsImage.GetBitmapAsync(cancellationToken).ConfigureAwait(false);

            // TODO: 実際のAdvancedImageFactory実装を見つけて適切に置き換える
            // 現時点では簡易実装として、必要最小限のメソッドを持つスタブを作成
            var simpleAdvancedImage = new SimpleAdvancedImageAdapter(bitmap, _logger);

            _logger.LogDebug("✅ [PHASE5.2] IWindowsImage → IAdvancedImage async変換完了");

            return simpleAdvancedImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE5.2] IWindowsImage → IAdvancedImage async変換失敗: {ErrorMessage}", ex.Message);
            throw;
        }
    }
}

/// <summary>
/// IAdvancedImage の簡易実装アダプター
/// AdaptiveTextRegionDetector が必要とする最小限のメソッドのみ実装
///
/// 注意: 完全な実装ではなく、TextRegionDetectorAdapter専用の簡易版
/// </summary>
internal sealed class SimpleAdvancedImageAdapter : IAdvancedImage, IDisposable
{
    private readonly Bitmap _bitmap;
    private readonly ILogger _logger;
    private bool _disposed;

    public int Width => _bitmap.Width;
    public int Height => _bitmap.Height;
    public ImageFormat Format => ImageFormat.Rgba32; // 簡易実装
    public DateTime CreatedAt => DateTime.Now;
    public bool IsGrayscale => false; // 簡易実装
    public int BitsPerPixel => 32; // 簡易実装
    public int ChannelCount => 4; // 簡易実装

    /// <summary>
    /// Phase 2.5: ROI座標変換対応 - 簡易実装アダプターなのでnull
    /// </summary>
    public System.Drawing.Rectangle? CaptureRegion => null;

    public ImagePixelFormat PixelFormat => ImagePixelFormat.Rgba32; // IImage要求

    public SimpleAdvancedImageAdapter(Bitmap bitmap, ILogger logger)
    {
        _bitmap = new Bitmap(bitmap); // コピーを作成
        _logger = logger;
    }

    // IAdvancedImage の必須メソッド（AdaptiveTextRegionDetectorで使用される可能性があるもの）
    public Color GetPixel(int x, int y) => _bitmap.GetPixel(x, y);
    public void SetPixel(int x, int y, Color color) => _bitmap.SetPixel(x, y, color);

    // 非同期メソッドは簡易実装
    public Task<IAdvancedImage> ApplyFilterAsync(IImageFilter filter) => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<IAdvancedImage> ApplyFiltersAsync(IEnumerable<IImageFilter> filters) => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<int[]> ComputeHistogramAsync(ColorChannel channel = ColorChannel.Luminance) => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<IAdvancedImage> ToGrayscaleAsync() => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public IAdvancedImage ToGrayscale() => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<IAdvancedImage> ToBinaryAsync(byte threshold) => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<IAdvancedImage> ExtractRegionAsync(Baketa.Core.Abstractions.Memory.Rectangle rectangle) => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<IAdvancedImage> OptimizeForOcrAsync() => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<IAdvancedImage> OptimizeForOcrAsync(OcrImageOptions options) => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<float> CalculateSimilarityAsync(IImage other) => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<float> EvaluateTextProbabilityAsync(Baketa.Core.Abstractions.Memory.Rectangle rectangle) => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<IAdvancedImage> RotateAsync(float degrees) => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<IAdvancedImage> EnhanceAsync(ImageEnhancementOptions options) => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");
    public Task<List<Baketa.Core.Abstractions.Memory.Rectangle>> DetectTextRegionsAsync() => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");

    // IImage 必須メソッド
    public async Task<byte[]> ToByteArrayAsync()
    {
        using var stream = new MemoryStream();
        _bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    public ReadOnlyMemory<byte> GetImageMemory() => ToByteArrayAsync().Result;

    /// <summary>
    /// 🔥 [ULTRATHINK_PHASE7] 内部Bitmapへのアクセスを提供 - PNG round-trip回避
    ///
    /// 使用方法:
    /// <code>
    /// if (advancedImage is SimpleAdvancedImageAdapter adapter)
    /// {
    ///     var bitmap = adapter.GetUnderlyingBitmap();
    ///     var safeImage = _safeImageFactory.CreateFromBitmap(bitmap, bitmap.Width, bitmap.Height);
    ///     // SafeImageFactory.CreateFromBitmap()がデータをコピーするため、この後すぐにDispose()可能
    /// }
    /// </code>
    /// </summary>
    /// <returns>内部Bitmapの参照（呼び出し側はCreateFromBitmap()でデータコピー後にDispose推奨）</returns>
    public Bitmap GetUnderlyingBitmap() => _bitmap;

    /// <summary>
    /// 🔥 [PHASE5.2G-A] LockPixelData (SimpleAdvancedImageAdapter is adapter-only, not supported)
    /// </summary>
    public PixelDataLock LockPixelData() => throw new NotSupportedException("SimpleAdvancedImageAdapter does not support LockPixelData");

    public IImage Clone() => new SimpleAdvancedImageAdapter(_bitmap, _logger);
    public Task<IImage> ResizeAsync(int width, int height) => throw new NotImplementedException("SimpleAdvancedImageAdapter: 簡易実装では未対応");

    public void Dispose()
    {
        if (!_disposed)
        {
            _bitmap?.Dispose();
            _disposed = true;
        }
    }
}