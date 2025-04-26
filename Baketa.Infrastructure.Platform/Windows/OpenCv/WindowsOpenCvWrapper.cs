using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading.Tasks;
// IImageFactoryの名前空間競合を解決するために明示的に指定
using FactoryImageFactory = Baketa.Core.Abstractions.Factories.IImageFactory;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.Platform.Windows.OpenCv.Exceptions;
using Baketa.Infrastructure.Platform.Windows.OpenCv.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using Size = System.Drawing.Size;
using OCVSize = OpenCvSharp.Size;
using Point = System.Drawing.Point;
using Mat = OpenCvSharp.Mat;
// ImageFormatの名前空間競合を解決するためにエイリアスを指定
using CoreImageFormat = Baketa.Core.Abstractions.Imaging.ImageFormat;
// LogLevelの名前空間競合を解決
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Baketa.Infrastructure.Platform.Windows.OpenCv
{
    /// <summary>
    /// Windows環境でのOpenCVラッパー実装
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsOpenCvWrapper : IOpenCvWrapper
    {
        private readonly ILogger<WindowsOpenCvWrapper> _logger;
        private readonly FactoryImageFactory _imageFactory;
        private readonly OpenCvOptions _options;
        private bool _disposed;

        // --- LoggerMessage Definitions ---
        // 初期化ログ
        private static readonly Action<ILogger, int, Exception?> _logInitialization =
            LoggerMessage.Define<int>(
                MsLogLevel.Information,
                new EventId(1000, "Initialization"),
                "OpenCVラッパーを初期化しました: ThreadCount={ThreadCount}");
                
        // グレースケール変換ログ
        private static readonly Action<ILogger, Exception?> _logGrayscaleProcessingError =
            LoggerMessage.Define(
                MsLogLevel.Error,
                new EventId(1, nameof(ConvertToGrayscaleAsync)),
                "グレースケール変換中にOpenCVエラーが発生しました");

        private static readonly Action<ILogger, Exception?> _logGrayscaleUnexpectedError =
            LoggerMessage.Define(
                MsLogLevel.Error,
                new EventId(2, nameof(ConvertToGrayscaleAsync)),
                "グレースケール変換中に予期しないエラーが発生しました");

        // 閾値処理ログ
        private static readonly Action<ILogger, Exception?> _logThresholdError =
            LoggerMessage.Define(
                MsLogLevel.Error,
                new EventId(3, nameof(ApplyThresholdAsync)),
                "閾値処理中にOpenCVエラーが発生しました");

        private static readonly Action<ILogger, Exception?> _logThresholdUnexpectedError =
            LoggerMessage.Define(
                MsLogLevel.Error,
                new EventId(4, nameof(ApplyThresholdAsync)),
                "閾値処理中に予期しないエラーが発生しました");

        // 汎用デバッグログ
        private static readonly Action<ILogger, string, Exception?> _logDebugMessage = // Renamed from _logDebug to avoid conflict
            LoggerMessage.Define<string>(
                MsLogLevel.Debug,
                new EventId(100, "Debug"),
                "{Message}");

        // 変換エラーログ
        private static readonly Action<ILogger, Exception?> _logConversionError =
            LoggerMessage.Define(
                MsLogLevel.Error,
                new EventId(200, "ConversionError"),
                "IAdvancedImageからMatへの変換中にエラーが発生しました");

        private static readonly Action<ILogger, Exception?> _logMatConversionError =
            LoggerMessage.Define(
                MsLogLevel.Error,
                new EventId(201, "MatConversionError"),
                "MatからIAdvancedImageへの変換中にエラーが発生しました");

        private static readonly Action<ILogger, Exception?> _logEmptyMatWarning =
            LoggerMessage.Define(
                MsLogLevel.Warning,
                new EventId(250, "EmptyMat"),
                "Attempted to convert an empty Mat to IImage.");


        /// <summary>
        /// まとめて追加が必要な、その他のロガーメッセージデリゲートを定義
        /// </summary>
        private static class LogMessages
        {
            // --- デバッグログ ---
            private static readonly Action<ILogger, string, Exception?> _logAdaptiveThresholdStart =
                LoggerMessage.Define<string>(MsLogLevel.Debug, new EventId(10, nameof(ApplyAdaptiveThresholdAsync)), "{Message}");
            private static readonly Action<ILogger, string, Exception?> _logCannyEdgeStart =
                LoggerMessage.Define<string>(MsLogLevel.Debug, new EventId(11, nameof(ApplyCannyEdgeAsync)), "{Message}");
            private static readonly Action<ILogger, string, Exception?> _logGaussianBlurStart =
                LoggerMessage.Define<string>(MsLogLevel.Debug, new EventId(12, nameof(ApplyGaussianBlurAsync)), "{Message}");
            private static readonly Action<ILogger, string, Exception?> _logMedianBlurStart =
                LoggerMessage.Define<string>(MsLogLevel.Debug, new EventId(13, nameof(ApplyMedianBlurAsync)), "{Message}");
            private static readonly Action<ILogger, string, Exception?> _logMorphologyStart =
                LoggerMessage.Define<string>(MsLogLevel.Debug, new EventId(14, nameof(ApplyMorphologyAsync)), "{Message}");
            private static readonly Action<ILogger, string, Exception?> _logTextDetectionStart =
                LoggerMessage.Define<string>(MsLogLevel.Debug, new EventId(15, nameof(DetectTextRegionsAsync)), "{Message}");
            private static readonly Action<ILogger, int, Exception?> _logTextDetectionComplete =
                LoggerMessage.Define<int>(MsLogLevel.Debug, new EventId(16, "TextDetectionComplete"), "テキスト領域検出が完了しました: {Count}個の領域を検出");
            private static readonly Action<ILogger, string, Exception?> _logMethodNotImplemented =
                LoggerMessage.Define<string>(MsLogLevel.Warning, new EventId(17, "MethodNotImplemented"), "{Method}は開発中です");

            // --- エラーログ ---
            // AdaptiveThreshold
            private static readonly Action<ILogger, Exception?> _logAdaptiveThresholdError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(50, nameof(ApplyAdaptiveThresholdAsync)), "適応的閾値処理中にOpenCVエラーが発生しました");
            private static readonly Action<ILogger, Exception?> _logAdaptiveThresholdUnexpectedError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(51, nameof(ApplyAdaptiveThresholdAsync)), "適応的閾値処理中に予期しないエラーが発生しました");
            // CannyEdge
            private static readonly Action<ILogger, Exception?> _logCannyEdgeError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(52, nameof(ApplyCannyEdgeAsync)), "Cannyエッジ検出中にOpenCVエラーが発生しました");
            private static readonly Action<ILogger, Exception?> _logCannyEdgeUnexpectedError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(53, nameof(ApplyCannyEdgeAsync)), "Cannyエッジ検出中に予期しないエラーが発生しました");
            // GaussianBlur
            private static readonly Action<ILogger, Exception?> _logGaussianBlurError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(54, nameof(ApplyGaussianBlurAsync)), "ガウシアンブラー適用中にOpenCVエラーが発生しました");
            private static readonly Action<ILogger, Exception?> _logGaussianBlurUnexpectedError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(55, nameof(ApplyGaussianBlurAsync)), "ガウシアンブラー適用中に予期しないエラーが発生しました");
            // MedianBlur
            private static readonly Action<ILogger, Exception?> _logMedianBlurError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(56, nameof(ApplyMedianBlurAsync)), "メディアンブラー適用中にOpenCVエラーが発生しました");
            private static readonly Action<ILogger, Exception?> _logMedianBlurUnexpectedError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(57, nameof(ApplyMedianBlurAsync)), "メディアンブラー適用中に予期しないエラーが発生しました");
            // Morphology
            private static readonly Action<ILogger, Exception?> _logMorphologyError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(58, nameof(ApplyMorphologyAsync)), "モルフォロジー演算中にOpenCVエラーが発生しました");
            private static readonly Action<ILogger, Exception?> _logMorphologyUnexpectedError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(59, nameof(ApplyMorphologyAsync)), "モルフォロジー演算中に予期しないエラーが発生しました");
            // TextDetection
            private static readonly Action<ILogger, Exception?> _logTextDetectionError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(60, nameof(DetectTextRegionsAsync)), "テキスト領域検出中にOpenCVエラーが発生しました");
            private static readonly Action<ILogger, Exception?> _logTextDetectionUnexpectedError =
                LoggerMessage.Define(MsLogLevel.Error, new EventId(61, nameof(DetectTextRegionsAsync)), "テキスト領域検出中に予期しないエラーが発生しました");


            // --- 公開メソッド ---
            // Debug
            public static void AdaptiveThresholdStart(ILogger logger, string message) => _logAdaptiveThresholdStart(logger, message, null);
            public static void CannyEdgeStart(ILogger logger, string message) => _logCannyEdgeStart(logger, message, null);
            public static void GaussianBlurStart(ILogger logger, string message) => _logGaussianBlurStart(logger, message, null);
            public static void MedianBlurStart(ILogger logger, string message) => _logMedianBlurStart(logger, message, null);
            public static void MorphologyStart(ILogger logger, string message) => _logMorphologyStart(logger, message, null);
            public static void TextDetectionStart(ILogger logger, string message) => _logTextDetectionStart(logger, message, null);
            public static void TextDetectionComplete(ILogger logger, int count) => _logTextDetectionComplete(logger, count, null);
            public static void MethodNotImplemented(ILogger logger, string method) => _logMethodNotImplemented(logger, method, null);
            // Error
            public static void AdaptiveThresholdError(ILogger logger, Exception ex) => _logAdaptiveThresholdError(logger, ex);
            public static void AdaptiveThresholdUnexpectedError(ILogger logger, Exception ex) => _logAdaptiveThresholdUnexpectedError(logger, ex);
            public static void CannyEdgeError(ILogger logger, Exception ex) => _logCannyEdgeError(logger, ex);
            public static void CannyEdgeUnexpectedError(ILogger logger, Exception ex) => _logCannyEdgeUnexpectedError(logger, ex);
            public static void GaussianBlurError(ILogger logger, Exception ex) => _logGaussianBlurError(logger, ex);
            public static void GaussianBlurUnexpectedError(ILogger logger, Exception ex) => _logGaussianBlurUnexpectedError(logger, ex);
            public static void MedianBlurError(ILogger logger, Exception ex) => _logMedianBlurError(logger, ex);
            public static void MedianBlurUnexpectedError(ILogger logger, Exception ex) => _logMedianBlurUnexpectedError(logger, ex);
            public static void MorphologyError(ILogger logger, Exception ex) => _logMorphologyError(logger, ex);
            public static void MorphologyUnexpectedError(ILogger logger, Exception ex) => _logMorphologyUnexpectedError(logger, ex);
            public static void TextDetectionError(ILogger logger, Exception ex) => _logTextDetectionError(logger, ex);
            public static void TextDetectionUnexpectedError(ILogger logger, Exception ex) => _logTextDetectionUnexpectedError(logger, ex);
        }
        // --- End LoggerMessage Definitions ---


        /// <summary>
        /// WindowsOpenCvWrapperのインスタンスを初期化します
        /// </summary>
        /// <param name="logger">ロガー</param>
        /// <param name="imageFactory">画像ファクトリ</param>
        /// <param name="options">OpenCV設定オプション</param>
        public WindowsOpenCvWrapper(
            ILogger<WindowsOpenCvWrapper> logger,
            FactoryImageFactory imageFactory,
            IOptions<OpenCvOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
            _options = options?.Value ?? new OpenCvOptions();

            // スレッド数の設定
            if (_options.DefaultThreadCount > 0)
            {
                Cv2.SetNumThreads(_options.DefaultThreadCount);
            }

            // 初期化ログの記録
            _logInitialization(_logger, Cv2.GetNumThreads(), null);
        }

        /// <summary>
        /// デバッグログを記録するヘルパーメソッド
        /// </summary>
        private static void LogDebug(ILogger logger, string message)
        {
            // Use the renamed delegate
            _logDebugMessage(logger, message, null);
        }
        // Removed duplicate LogDebug method

        /// <summary>
        /// 画像をグレースケールに変換します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <returns>グレースケール変換された画像</returns>
        public async Task<IAdvancedImage> ConvertToGrayscaleAsync(IAdvancedImage source)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(source);

            try
            {
                LogDebug(_logger, "画像をグレースケールに変換します"); // Use helper method

                // OpenCVのMatに変換
                using var sourceMat = await ConvertToMatAsync(source).ConfigureAwait(false);
                using var grayMat = new Mat();

                // グレースケール変換
                Cv2.CvtColor(sourceMat, grayMat, ColorConversionCodes.BGR2GRAY);

                // 結果をIAdvancedImageに変換して返す
                var result = await ConvertFromMatAsync(grayMat, false).ConfigureAwait(false);
                return result as IAdvancedImage ??
                    throw new InvalidOperationException("変換結果がIAdvancedImageにキャストできませんでした");
            }
            catch (OpenCvSharpException ex)
            {
                _logGrayscaleProcessingError(_logger, ex); // Use defined delegate
                throw new OcrProcessingException("グレースケール変換に失敗しました", ex);
            }
            catch (Exception ex)
            {
                _logGrayscaleUnexpectedError(_logger, ex); // Use defined delegate
                throw;
            }
        }

        /// <summary>
        /// 画像に閾値処理を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="threshold">閾値</param>
        /// <param name="maxValue">最大値</param>
        /// <param name="type">閾値処理タイプ</param>
        /// <returns>閾値処理された画像</returns>
        public async Task<IAdvancedImage> ApplyThresholdAsync(IAdvancedImage source, double threshold, double maxValue, ThresholdType type)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(source);

            try
            {
                LogDebug(_logger, $"閾値処理を適用します: 閾値={threshold}, 最大値={maxValue}, タイプ={type}"); // Use helper method

                // OpenCVのMatに変換
                using var sourceMat = await ConvertToMatAsync(source).ConfigureAwait(false);
                using var grayMat = new Mat();
                using var thresholdMat = new Mat();

                // 入力画像がグレースケールでない場合は変換
                if (sourceMat.Channels() != 1)
                {
                    Cv2.CvtColor(sourceMat, grayMat, ColorConversionCodes.BGR2GRAY);
                }
                else
                {
                    sourceMat.CopyTo(grayMat);
                }

                // 閾値処理タイプの変換
                var openCvThresholdType = OpenCvExtensions.ConvertThresholdType(type);

                // 閾値処理の適用
                Cv2.Threshold(grayMat, thresholdMat, threshold, maxValue, openCvThresholdType);

                // 結果をIAdvancedImageに変換して返す
                var result = await ConvertFromMatAsync(thresholdMat, false).ConfigureAwait(false);
                return result as IAdvancedImage ??
                    throw new InvalidOperationException("変換結果がIAdvancedImageにキャストできませんでした");
            }
            catch (OpenCvSharpException ex)
            {
                _logThresholdError(_logger, ex); // Use defined delegate
                throw new OcrProcessingException("閾値処理に失敗しました", ex);
            }
            catch (Exception ex)
            {
                _logThresholdUnexpectedError(_logger, ex); // Use defined delegate
                throw;
            }
        }

        /// <summary>
        /// 画像に適応的閾値処理を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="maxValue">最大値</param>
        /// <param name="adaptiveType">適応的閾値処理タイプ</param>
        /// <param name="thresholdType">閾値処理タイプ</param>
        /// <param name="blockSize">ブロックサイズ</param>
        /// <param name="c">定数</param>
        /// <returns>閾値処理された画像</returns>
        public async Task<IAdvancedImage> ApplyAdaptiveThresholdAsync(IAdvancedImage source, double maxValue, AdaptiveThresholdType adaptiveType, ThresholdType thresholdType, int blockSize, double c)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(source);

            // blockSizeは奇数である必要がある
            if (blockSize % 2 == 0) blockSize += 1;

            try
            {
                var message = $"適応的閾値処理を適用します: 最大値={maxValue}, 適応タイプ={adaptiveType}, 閾値タイプ={thresholdType}, ブロックサイズ={blockSize}, 定数={c}";
                LogMessages.AdaptiveThresholdStart(_logger, message); // Use LogMessages delegate

                // OpenCVのMatに変換
                using var sourceMat = await ConvertToMatAsync(source).ConfigureAwait(false);
                using var grayMat = new Mat();
                using var thresholdMat = new Mat();

                // 入力画像がグレースケールでない場合は変換
                if (sourceMat.Channels() != 1)
                {
                    Cv2.CvtColor(sourceMat, grayMat, ColorConversionCodes.BGR2GRAY);
                }
                else
                {
                    sourceMat.CopyTo(grayMat);
                }

                // 適応的閾値処理タイプと閾値処理タイプの変換
                var openCvAdaptiveType = OpenCvExtensions.ConvertAdaptiveThresholdType(adaptiveType);
                var openCvThresholdType = OpenCvExtensions.ConvertBinaryThresholdType(thresholdType);

                // 適応的閾値処理の適用
                Cv2.AdaptiveThreshold(grayMat, thresholdMat, maxValue, openCvAdaptiveType, openCvThresholdType, blockSize, c);

                // 結果をIAdvancedImageに変換して返す
                var result = await ConvertFromMatAsync(thresholdMat, false).ConfigureAwait(false);
                // Added null check
                return result as IAdvancedImage ??
                       throw new InvalidOperationException("変換結果がIAdvancedImageにキャストできませんでした");
            }
            catch (OpenCvSharpException ex)
            {
                LogMessages.AdaptiveThresholdError(_logger, ex); // Use LogMessages delegate
                throw new OcrProcessingException("適応的閾値処理に失敗しました", ex);
            }
            catch (Exception ex)
            {
                LogMessages.AdaptiveThresholdUnexpectedError(_logger, ex); // Use LogMessages delegate
                throw;
            }
        }

        /// <summary>
        /// 画像にガウシアンブラーを適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="kernelSize">カーネルサイズ</param>
        /// <param name="sigmaX">X方向のシグマ値</param>
        /// <param name="sigmaY">Y方向のシグマ値（0の場合はsigmaXと同じ値が使用される）</param>
        /// <returns>ブラー処理された画像</returns>
        public async Task<IAdvancedImage> ApplyGaussianBlurAsync(IAdvancedImage source, Size kernelSize, double sigmaX = 0, double sigmaY = 0)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(source);

            // カーネルサイズは奇数である必要がある
            var kSize = new OCVSize(
                kernelSize.Width % 2 == 0 ? kernelSize.Width + 1 : kernelSize.Width,
                kernelSize.Height % 2 == 0 ? kernelSize.Height + 1 : kernelSize.Height);

            try
            {
                var message = $"ガウシアンブラーを適用します: カーネルサイズ=({kSize.Width}, {kSize.Height}), SigmaX={sigmaX}, SigmaY={sigmaY}";
                LogMessages.GaussianBlurStart(_logger, message); // Use LogMessages delegate

                // OpenCVのMatに変換
                using var sourceMat = await ConvertToMatAsync(source).ConfigureAwait(false);
                using var blurredMat = new Mat();

                // ガウシアンブラーの適用
                Cv2.GaussianBlur(sourceMat, blurredMat, kSize, sigmaX, sigmaY);

                // 結果をIAdvancedImageに変換して返す
                var result = await ConvertFromMatAsync(blurredMat, false).ConfigureAwait(false);
                return result as IAdvancedImage ??
                       throw new InvalidOperationException("変換結果がIAdvancedImageにキャストできませんでした");
            }
            catch (OpenCvSharpException ex)
            {
                LogMessages.GaussianBlurError(_logger, ex); // Use LogMessages delegate
                throw new OcrProcessingException("ガウシアンブラー適用に失敗しました", ex);
            }
            catch (Exception ex)
            {
                LogMessages.GaussianBlurUnexpectedError(_logger, ex); // Use LogMessages delegate
                throw;
            }
        }

        /// <summary>
        /// 画像にメディアンブラーを適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="kernelSize">カーネルサイズ</param>
        /// <returns>ブラー処理された画像</returns>
        public async Task<IAdvancedImage> ApplyMedianBlurAsync(IAdvancedImage source, int kernelSize)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(source);

            // カーネルサイズは奇数である必要がある
            if (kernelSize % 2 == 0) kernelSize += 1;

            try
            {
                var message = $"メディアンブラーを適用します: カーネルサイズ={kernelSize}";
                LogMessages.MedianBlurStart(_logger, message); // Use LogMessages delegate

                // OpenCVのMatに変換
                using var sourceMat = await ConvertToMatAsync(source).ConfigureAwait(false);
                using var blurredMat = new Mat();

                // メディアンブラーの適用
                Cv2.MedianBlur(sourceMat, blurredMat, kernelSize);

                // 結果をIAdvancedImageに変換して返す
                var result = await ConvertFromMatAsync(blurredMat, false).ConfigureAwait(false);
                return result as IAdvancedImage ??
                       throw new InvalidOperationException("変換結果がIAdvancedImageにキャストできませんでした");
            }
            catch (OpenCvSharpException ex)
            {
                LogMessages.MedianBlurError(_logger, ex); // Use LogMessages delegate
                throw new OcrProcessingException("メディアンブラー適用に失敗しました", ex);
            }
            catch (Exception ex)
            {
                LogMessages.MedianBlurUnexpectedError(_logger, ex); // Use LogMessages delegate
                throw;
            }
        }

        /// <summary>
        /// 画像に対してCannyエッジ検出を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="threshold1">下側閾値</param>
        /// <param name="threshold2">上側閾値</param>
        /// <param name="apertureSize">アパーチャーサイズ</param>
        /// <param name="l2Gradient">L2勾配を使用するかどうか</param>
        /// <returns>エッジ検出結果画像</returns>
        public async Task<IAdvancedImage> ApplyCannyEdgeAsync(IAdvancedImage source, double threshold1, double threshold2, int apertureSize = 3, bool l2Gradient = false)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(source);

            try
            {
                var message = $"Cannyエッジ検出を適用します: 閾値1={threshold1}, 閾値2={threshold2}, アパーチャーサイズ={apertureSize}, L2勾配={l2Gradient}";
                LogMessages.CannyEdgeStart(_logger, message); // Use LogMessages delegate

                // OpenCVのMatに変換
                using var sourceMat = await ConvertToMatAsync(source).ConfigureAwait(false);
                using var grayMat = new Mat();
                using var edgeMat = new Mat();

                // 入力画像がグレースケールでない場合は変換
                if (sourceMat.Channels() != 1)
                {
                    Cv2.CvtColor(sourceMat, grayMat, ColorConversionCodes.BGR2GRAY);
                }
                else
                {
                    sourceMat.CopyTo(grayMat);
                }

                // Cannyエッジ検出の適用
                Cv2.Canny(grayMat, edgeMat, threshold1, threshold2, apertureSize, l2Gradient);

                // 結果をIAdvancedImageに変換して返す
                var result = await ConvertFromMatAsync(edgeMat, false).ConfigureAwait(false);
                return result as IAdvancedImage ??
                       throw new InvalidOperationException("変換結果がIAdvancedImageにキャストできませんでした");
            }
            catch (OpenCvSharpException ex)
            {
                LogMessages.CannyEdgeError(_logger, ex); // Use LogMessages delegate
                throw new OcrProcessingException("Cannyエッジ検出に失敗しました", ex);
            }
            catch (Exception ex)
            {
                LogMessages.CannyEdgeUnexpectedError(_logger, ex); // Use LogMessages delegate
                throw;
            }
        }

        /// <summary>
        /// 画像にモルフォロジー演算を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="morphType">モルフォロジー演算タイプ</param>
        /// <param name="kernelSize">カーネルサイズ</param>
        /// <param name="iterations">反復回数</param>
        /// <returns>モルフォロジー演算結果画像</returns>
        public async Task<IAdvancedImage> ApplyMorphologyAsync(IAdvancedImage source, MorphType morphType, Size kernelSize, int iterations = 1)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(source);

            try
            {
                var message = $"モルフォロジー演算を適用します: タイプ={morphType}, カーネルサイズ=({kernelSize.Width}, {kernelSize.Height}), 反復回数={iterations}";
                LogMessages.MorphologyStart(_logger, message); // Use LogMessages delegate

                // OpenCVのMatに変換
                using var sourceMat = await ConvertToMatAsync(source).ConfigureAwait(false);
                using var morphMat = new Mat();

                // 構造要素（カーネル）の作成
                using var kernel = Cv2.GetStructuringElement(
                    MorphShapes.Rect,
                    new OCVSize(kernelSize.Width, kernelSize.Height));

                // モルフォロジー演算タイプの変換
                var openCvMorphType = OpenCvExtensions.ConvertMorphType(morphType);

                // モルフォロジー演算の適用
                Cv2.MorphologyEx(sourceMat, morphMat, openCvMorphType, kernel, null, iterations);

                // 結果をIAdvancedImageに変換して返す
                var result = await ConvertFromMatAsync(morphMat, false).ConfigureAwait(false);
                return result as IAdvancedImage ??
                       throw new InvalidOperationException("変換結果がIAdvancedImageにキャストできませんでした");
            }
            catch (OpenCvSharpException ex)
            {
                LogMessages.MorphologyError(_logger, ex); // Use LogMessages delegate
                throw new OcrProcessingException("モルフォロジー演算に失敗しました", ex);
            }
            catch (Exception ex)
            {
                LogMessages.MorphologyUnexpectedError(_logger, ex); // Use LogMessages delegate
                throw;
            }
        }

        /// <summary>
        /// 画像からテキスト領域の候補となる矩形を検出します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="method">テキスト検出方法</param>
        /// <param name="parameters">テキスト検出パラメータ（nullの場合はデフォルト値が使用される）</param>
        /// <returns>検出された矩形のリスト</returns>
        public async Task<IReadOnlyList<Rectangle>> DetectTextRegionsAsync(IAdvancedImage source, TextDetectionMethod method, TextDetectionParams? parameters = null)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(source);

            // パラメータがnullの場合はデフォルト値を使用
            parameters ??= method switch
            {
                TextDetectionMethod.Mser => _options.DefaultMserParameters,
                TextDetectionMethod.ConnectedComponents => _options.DefaultConnectedComponentsParameters,
                TextDetectionMethod.Contours => _options.DefaultContoursParameters,
                TextDetectionMethod.EdgeBased => _options.DefaultEdgeBasedParameters,
                _ => TextDetectionParams.CreateForMethod(method)
            };

            try
            {
                var message = $"テキスト領域検出を開始します: 検出方法={method}";
                LogMessages.TextDetectionStart(_logger, message); // Use LogMessages delegate

                // OpenCVのMatに変換
                using var sourceMat = await ConvertToMatAsync(source).ConfigureAwait(false);
                using var grayMat = new Mat();

                // 入力画像がグレースケールでない場合は変換
                if (sourceMat.Channels() != 1)
                {
                    Cv2.CvtColor(sourceMat, grayMat, ColorConversionCodes.BGR2GRAY);
                }
                else
                {
                    sourceMat.CopyTo(grayMat);
                }

                // 検出方法に応じた処理
                var rectangles = method switch
                {
                    TextDetectionMethod.Mser => await DetectWithMserAsync(grayMat, parameters).ConfigureAwait(false),
                    TextDetectionMethod.ConnectedComponents => await DetectWithConnectedComponentsAsync(grayMat, parameters, _logger).ConfigureAwait(false),
                    TextDetectionMethod.Contours => await DetectWithContoursAsync(grayMat, parameters, _logger).ConfigureAwait(false),
                    TextDetectionMethod.EdgeBased => await DetectWithEdgeBasedAsync(grayMat, parameters, _logger).ConfigureAwait(false),
                    _ => throw new ArgumentException($"未サポートのテキスト検出方法: {method}", nameof(method))
                };

                LogMessages.TextDetectionComplete(_logger, rectangles.Count); // Use LogMessages delegate

                return rectangles;
            }
            catch (OpenCvSharpException ex)
            {
                LogMessages.TextDetectionError(_logger, ex); // Use LogMessages delegate
                throw new OcrProcessingException("テキスト領域検出に失敗しました", ex);
            }
            catch (Exception ex)
            {
                LogMessages.TextDetectionUnexpectedError(_logger, ex); // Use LogMessages delegate
                throw;
            }
        }

        #region テキスト領域検出の実装

        private static async Task<List<Rectangle>> DetectWithMserAsync(Mat grayMat, TextDetectionParams parameters)
        {
            using var mser = OpenCvSharp.MSER.Create(
                delta: parameters.MserDelta,
                minArea: parameters.MserMinArea,
                maxArea: parameters.MserMaxArea);

            mser.DetectRegions(grayMat, out OpenCvSharp.Point[][] msers, out _);

            var rectangles = new List<Rectangle>();
            foreach (var region in msers)
            {
                var rect = Cv2.BoundingRect(region);

                // サイズとアスペクト比によるフィルタリング
                if (rect.Width < parameters.MinWidth || rect.Height < parameters.MinHeight)
                    continue;

                float aspectRatio = rect.Width / (float)rect.Height;
                if (aspectRatio < parameters.MinAspectRatio || aspectRatio > parameters.MaxAspectRatio)
                    continue;

                rectangles.Add(new Rectangle(rect.X, rect.Y, rect.Width, rect.Height));
            }

            // 重複する矩形のマージ
            return await Task.FromResult(MergeOverlappingRectangles(rectangles, parameters.MergeThreshold)).ConfigureAwait(false);
        }

        private static async Task<List<Rectangle>> DetectWithConnectedComponentsAsync(Mat _1, TextDetectionParams _2, ILogger logger)
        {
            // 簡易実装（最小限の機能のみ）
            LogMessages.MethodNotImplemented(logger, "連結成分分析による検出");
            return await Task.FromResult(new List<Rectangle>()).ConfigureAwait(false);
        }

        private static async Task<List<Rectangle>> DetectWithContoursAsync(Mat _1, TextDetectionParams _2, ILogger logger)
        {
            // 簡易実装（最小限の機能のみ）
            LogMessages.MethodNotImplemented(logger, "輪郭ベースの検出");
            return await Task.FromResult(new List<Rectangle>()).ConfigureAwait(false);
        }

        private static async Task<List<Rectangle>> DetectWithEdgeBasedAsync(Mat _1, TextDetectionParams _2, ILogger logger)
        {
            // 簡易実装（最小限の機能のみ）
            LogMessages.MethodNotImplemented(logger, "エッジベースの検出");
            return await Task.FromResult(new List<Rectangle>()).ConfigureAwait(false);
        }

        private static List<Rectangle> MergeOverlappingRectangles(List<Rectangle> rectangles, float overlapThreshold)
        {
            if (rectangles.Count == 0)
                return rectangles;

            var result = new List<Rectangle>();
            var processed = new bool[rectangles.Count];

            for (int i = 0; i < rectangles.Count; i++)
            {
                if (processed[i])
                    continue;

                var currentRect = rectangles[i];
                processed[i] = true;

                for (int j = i + 1; j < rectangles.Count; j++)
                {
                    if (processed[j])
                        continue;

                    var otherRect = rectangles[j];

                    // 重複領域の計算
                    var intersection = Rectangle.Intersect(currentRect, otherRect);
                    if (intersection.Width <= 0 || intersection.Height <= 0)
                        continue;

                    var intersectionArea = intersection.Width * intersection.Height;
                    var currentArea = currentRect.Width * currentRect.Height;
                    var otherArea = otherRect.Width * otherRect.Height;
                    var smallerArea = Math.Min(currentArea, otherArea);

                    // 重複率の計算
                    var overlapRatio = (float)intersectionArea / smallerArea;

                    if (overlapRatio > overlapThreshold)
                    {
                        // 矩形をマージ
                        currentRect = Rectangle.Union(currentRect, otherRect);
                        processed[j] = true;
                        // Reset the inner loop to re-check merged rectangle against all others
                        j = i;
                    }
                }

                result.Add(currentRect);
            }

            return result;
        }

        #endregion

        #region ヘルパーメソッド

        /// <summary>
        /// IAdvancedImageをOpenCVのMatに変換します
        /// </summary>
        public async Task<Mat> ConvertToMatAsync(IAdvancedImage image)
        {
            ArgumentNullException.ThrowIfNull(image);

            try
            {
                // IAdvancedImageから非同期でバイト配列を取得
                var imageData = await image.ToByteArrayAsync().ConfigureAwait(false);

                // バイト配列をMatに変換
                var result = OpenCvSharp.Mat.FromImageData(imageData);

                // 色チャンネルの順序が異なる場合は変換（RGB → BGR）
                if (image.Format == CoreImageFormat.Rgb24 ||
                    image.Format == CoreImageFormat.Rgba32)
                {
                    // Make sure the result mat is not empty and has enough channels
                    if (!result.Empty() && result.Channels() >= 3)
                    {
                        // Check target format based on channels
                        var conversionCode = result.Channels() == 4 ? ColorConversionCodes.RGBA2BGR : ColorConversionCodes.RGB2BGR;
                        Cv2.CvtColor(result, result, conversionCode);
                    }
                    else
                    {
                        // Log warning or handle cases where conversion isn't possible/needed
                        LogDebug(_logger, $"Skipping color conversion for image format {image.Format} with {result.Channels()} channels.");
                    }
                }
                else if (image.Format == CoreImageFormat.Grayscale8)
                {
                    // Ensure Mat is also grayscale if needed, though FromImageData might handle it
                    if (!result.Empty() && result.Channels() != 1)
                    {
                        using var tempMat = result.Clone(); // Clone to avoid modifying original if needed elsewhere
                        Cv2.CvtColor(tempMat, result, ColorConversionCodes.BGR2GRAY); // Assuming original might be BGR
                    }
                }


                return result;
            }
            catch (Exception ex)
            {
                _logConversionError(_logger, ex);
                throw new OcrProcessingException("IAdvancedImageからMatへの変換に失敗しました", ex);
            }
        }

        /// <summary>
        /// OpenCVのMatをIAdvancedImageに変換します
        /// </summary>
        public async Task<IImage> ConvertFromMatAsync(Mat mat, bool disposeSource = false)
        {
            ArgumentNullException.ThrowIfNull(mat);
            if (mat.Empty())
            {
                _logEmptyMatWarning(_logger, null);
                // Consider returning null or a default image, or throwing specific exception
                throw new ArgumentException("Input Mat cannot be empty.", nameof(mat));
            }


            try
            {
                // Determine the correct format based on Mat channels
                string formatExtension = mat.Channels() switch
                {
                    1 => ".png", // Grayscale - PNG supports grayscale well
                    3 => ".png", // BGR - PNG is lossless
                    4 => ".png", // BGRA - PNG supports alpha
                    _ => throw new NotSupportedException($"Mat with {mat.Channels()} channels is not supported for conversion.")
                };


                // Matをバイト配列に変換
                var imageBytes = mat.ToBytes(formatExtension);
                if (imageBytes == null || imageBytes.Length == 0)
                {
                    throw new OcrProcessingException($"Mat to byte array conversion failed (format: {formatExtension}).");
                }

                // バイト配列からIImageを非同期で作成
                var result = await _imageFactory.CreateFromBytesAsync(imageBytes).ConfigureAwait(false);

                // nullチェックを追加
                return result ?? throw new InvalidOperationException($"画像変換結果がnullです (format: {formatExtension})");
            }
            catch (Exception ex) when (ex is not OcrProcessingException && ex is not ArgumentException && ex is not NotSupportedException) // Avoid re-wrapping specific exceptions
            {
                _logMatConversionError(_logger, ex);
                throw new OcrProcessingException("MatからIAdvancedImageへの変換中に予期しないエラーが発生しました", ex);
            }
            finally
            {
                // リソース解放
                if (disposeSource && !mat.IsDisposed) // Check if already disposed
                {
                    mat.Dispose();
                }
            }
        }

        /// <summary>
        /// オブジェクトが破棄されている場合に例外をスローします
        /// </summary>
        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// マネージドおよびアンマネージドリソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放する場合はtrue</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // マネージドリソースを解放
                    // No managed resources to dispose currently.
                }

                // アンマネージドリソースを解放 (OpenCV Matなどは using ステートメントで管理)
                // No unmanaged resources held directly by this class instance.

                _disposed = true;
            }
        }

        // Removed extra closing brace '}' from the end of the original file
        #endregion
    }
}