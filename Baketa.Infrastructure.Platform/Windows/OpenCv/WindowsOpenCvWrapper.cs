using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
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

namespace Baketa.Infrastructure.Platform.Windows.OpenCv
{
    /// <summary>
    /// Windows環境でのOpenCVラッパー実装
    /// </summary>
    public class WindowsOpenCvWrapper : IOpenCvWrapper
    {
        private readonly ILogger<WindowsOpenCvWrapper> _logger;
        private readonly IImageFactory _imageFactory;
        private readonly OpenCvOptions _options;
        private bool _disposed;

        /// <summary>
        /// WindowsOpenCvWrapperのインスタンスを初期化します
        /// </summary>
        /// <param name="logger">ロガー</param>
        /// <param name="imageFactory">画像ファクトリ</param>
        /// <param name="options">OpenCV設定オプション</param>
        public WindowsOpenCvWrapper(
            ILogger<WindowsOpenCvWrapper> logger,
            IImageFactory imageFactory,
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
            
            _logger.LogInformation("OpenCVラッパーを初期化しました: ThreadCount={ThreadCount}", 
                Cv2.GetNumThreads());
        }

        /// <summary>
        /// 画像をグレースケールに変換します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <returns>グレースケール変換された画像</returns>
        public async Task<IAdvancedImage> ConvertToGrayscaleAsync(IAdvancedImage source)
        {
            ThrowIfDisposed();
            if (source == null) throw new ArgumentNullException(nameof(source));

            try
            {
                _logger.LogDebug("画像をグレースケールに変換します");
                
                // OpenCVのMatに変換
                using var sourceMat = ConvertToMat(source);
                using var grayMat = new Mat();
                
                // グレースケール変換
                Cv2.CvtColor(sourceMat, grayMat, ColorConversionCodes.BGR2GRAY);
                
                // 結果をIAdvancedImageに変換して返す
                return await Task.FromResult(ConvertFromMat(grayMat, false) as IAdvancedImage);
            }
            catch (OpenCvSharpException ex)
            {
                _logger.LogError(ex, "グレースケール変換中にOpenCVエラーが発生しました");
                throw new OcrProcessingException("グレースケール変換に失敗しました", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "グレースケール変換中に予期しないエラーが発生しました");
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
            if (source == null) throw new ArgumentNullException(nameof(source));

            try
            {
                _logger.LogDebug("閾値処理を適用します: 閾値={Threshold}, 最大値={MaxValue}, タイプ={Type}", threshold, maxValue, type);
                
                // OpenCVのMatに変換
                using var sourceMat = ConvertToMat(source);
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
                return await Task.FromResult(ConvertFromMat(thresholdMat, false) as IAdvancedImage);
            }
            catch (OpenCvSharpException ex)
            {
                _logger.LogError(ex, "閾値処理中にOpenCVエラーが発生しました");
                throw new OcrProcessingException("閾値処理に失敗しました", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "閾値処理中に予期しないエラーが発生しました");
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
            if (source == null) throw new ArgumentNullException(nameof(source));
            
            // blockSizeは奇数である必要がある
            if (blockSize % 2 == 0) blockSize += 1;

            try
            {
                _logger.LogDebug("適応的閾値処理を適用します: 最大値={MaxValue}, 適応タイプ={AdaptiveType}, 閾値タイプ={ThresholdType}, ブロックサイズ={BlockSize}, 定数={C}", 
                    maxValue, adaptiveType, thresholdType, blockSize, c);
                
                // OpenCVのMatに変換
                using var sourceMat = ConvertToMat(source);
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
                return await Task.FromResult(ConvertFromMat(thresholdMat, false) as IAdvancedImage);
            }
            catch (OpenCvSharpException ex)
            {
                _logger.LogError(ex, "適応的閾値処理中にOpenCVエラーが発生しました");
                throw new OcrProcessingException("適応的閾値処理に失敗しました", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "適応的閾値処理中に予期しないエラーが発生しました");
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
            if (source == null) throw new ArgumentNullException(nameof(source));
            
            // カーネルサイズは奇数である必要がある
            var kSize = new OCVSize(
                kernelSize.Width % 2 == 0 ? kernelSize.Width + 1 : kernelSize.Width,
                kernelSize.Height % 2 == 0 ? kernelSize.Height + 1 : kernelSize.Height);

            try
            {
                _logger.LogDebug("ガウシアンブラーを適用します: カーネルサイズ=({Width}, {Height}), SigmaX={SigmaX}, SigmaY={SigmaY}", 
                    kSize.Width, kSize.Height, sigmaX, sigmaY);
                
                // OpenCVのMatに変換
                using var sourceMat = ConvertToMat(source);
                using var blurredMat = new Mat();
                
                // ガウシアンブラーの適用
                Cv2.GaussianBlur(sourceMat, blurredMat, kSize, sigmaX, sigmaY);
                
                // 結果をIAdvancedImageに変換して返す
                return await Task.FromResult(ConvertFromMat(blurredMat, false) as IAdvancedImage);
            }
            catch (OpenCvSharpException ex)
            {
                _logger.LogError(ex, "ガウシアンブラー適用中にOpenCVエラーが発生しました");
                throw new OcrProcessingException("ガウシアンブラー適用に失敗しました", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ガウシアンブラー適用中に予期しないエラーが発生しました");
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
            if (source == null) throw new ArgumentNullException(nameof(source));
            
            // カーネルサイズは奇数である必要がある
            if (kernelSize % 2 == 0) kernelSize += 1;

            try
            {
                _logger.LogDebug("メディアンブラーを適用します: カーネルサイズ={KernelSize}", kernelSize);
                
                // OpenCVのMatに変換
                using var sourceMat = ConvertToMat(source);
                using var blurredMat = new Mat();
                
                // メディアンブラーの適用
                Cv2.MedianBlur(sourceMat, blurredMat, kernelSize);
                
                // 結果をIAdvancedImageに変換して返す
                return await Task.FromResult(ConvertFromMat(blurredMat, false) as IAdvancedImage);
            }
            catch (OpenCvSharpException ex)
            {
                _logger.LogError(ex, "メディアンブラー適用中にOpenCVエラーが発生しました");
                throw new OcrProcessingException("メディアンブラー適用に失敗しました", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "メディアンブラー適用中に予期しないエラーが発生しました");
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
            if (source == null) throw new ArgumentNullException(nameof(source));

            try
            {
                _logger.LogDebug("Cannyエッジ検出を適用します: 閾値1={Threshold1}, 閾値2={Threshold2}, アパーチャーサイズ={ApertureSize}, L2勾配={L2Gradient}", 
                    threshold1, threshold2, apertureSize, l2Gradient);
                
                // OpenCVのMatに変換
                using var sourceMat = ConvertToMat(source);
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
                return await Task.FromResult(ConvertFromMat(edgeMat, false) as IAdvancedImage);
            }
            catch (OpenCvSharpException ex)
            {
                _logger.LogError(ex, "Cannyエッジ検出中にOpenCVエラーが発生しました");
                throw new OcrProcessingException("Cannyエッジ検出に失敗しました", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannyエッジ検出中に予期しないエラーが発生しました");
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
            if (source == null) throw new ArgumentNullException(nameof(source));

            try
            {
                _logger.LogDebug("モルフォロジー演算を適用します: タイプ={MorphType}, カーネルサイズ=({Width}, {Height}), 反復回数={Iterations}", 
                    morphType, kernelSize.Width, kernelSize.Height, iterations);
                
                // OpenCVのMatに変換
                using var sourceMat = ConvertToMat(source);
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
                return await Task.FromResult(ConvertFromMat(morphMat, false) as IAdvancedImage);
            }
            catch (OpenCvSharpException ex)
            {
                _logger.LogError(ex, "モルフォロジー演算中にOpenCVエラーが発生しました");
                throw new OcrProcessingException("モルフォロジー演算に失敗しました", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "モルフォロジー演算中に予期しないエラーが発生しました");
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
        public async Task<IReadOnlyList<Rectangle>> DetectTextRegionsAsync(IAdvancedImage source, TextDetectionMethod method, TextDetectionParams parameters = null)
        {
            ThrowIfDisposed();
            if (source == null) throw new ArgumentNullException(nameof(source));
            
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
                _logger.LogDebug("テキスト領域検出を開始します: 検出方法={Method}", method);
                
                // OpenCVのMatに変換
                using var sourceMat = ConvertToMat(source);
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
                    TextDetectionMethod.Mser => await DetectWithMserAsync(grayMat, parameters),
                    TextDetectionMethod.ConnectedComponents => await DetectWithConnectedComponentsAsync(grayMat, parameters),
                    TextDetectionMethod.Contours => await DetectWithContoursAsync(grayMat, parameters),
                    TextDetectionMethod.EdgeBased => await DetectWithEdgeBasedAsync(grayMat, parameters),
                    _ => throw new ArgumentException($"未サポートのテキスト検出方法: {method}", nameof(method))
                };
                
                _logger.LogDebug("テキスト領域検出が完了しました: {Count}個の領域を検出", rectangles.Count);
                
                return rectangles;
            }
            catch (OpenCvSharpException ex)
            {
                _logger.LogError(ex, "テキスト領域検出中にOpenCVエラーが発生しました");
                throw new OcrProcessingException("テキスト領域検出に失敗しました", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "テキスト領域検出中に予期しないエラーが発生しました");
                throw;
            }
        }

        #region テキスト領域検出の実装

        private async Task<List<Rectangle>> DetectWithMserAsync(Mat grayMat, TextDetectionParams parameters)
        {
            using var mser = MSER.Create(
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
            return await Task.FromResult(MergeOverlappingRectangles(rectangles, parameters.MergeThreshold));
        }

        private async Task<List<Rectangle>> DetectWithConnectedComponentsAsync(Mat grayMat, TextDetectionParams parameters)
        {
            // 簡易実装（最小限の機能のみ）
            _logger.LogWarning("連結成分分析による検出は開発中です");
            return await Task.FromResult(new List<Rectangle>());
        }

        private async Task<List<Rectangle>> DetectWithContoursAsync(Mat grayMat, TextDetectionParams parameters)
        {
            // 簡易実装（最小限の機能のみ）
            _logger.LogWarning("輪郭ベースの検出は開発中です");
            return await Task.FromResult(new List<Rectangle>());
        }

        private async Task<List<Rectangle>> DetectWithEdgeBasedAsync(Mat grayMat, TextDetectionParams parameters)
        {
            // 簡易実装（最小限の機能のみ）
            _logger.LogWarning("エッジベースの検出は開発中です");
            return await Task.FromResult(new List<Rectangle>());
        }

        private List<Rectangle> MergeOverlappingRectangles(List<Rectangle> rectangles, float overlapThreshold)
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
        public Mat ConvertToMat(IAdvancedImage image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            
            try
            {
                // 実際の実装では、イメージデータの直接アクセスやメモリコピーを最小限にする最適化が必要
                // このサンプル実装ではシンプルな方法で変換
                
                // IAdvancedImageからバイト配列を取得
                var imageData = image.ToByteArrayAsync().GetAwaiter().GetResult();
                
                // バイト配列をMatに変換
                // 注: 実際の実装では、フォーマットに応じた適切な変換が必要
                var result = Mat.FromImageData(imageData);
                
                // 色チャンネルの順序が異なる場合は変換（RGB → BGR）
                if (image.Format == ImageFormat.Rgb24 || image.Format == ImageFormat.Rgba32)
                {
                    Cv2.CvtColor(result, result, ColorConversionCodes.RGB2BGR);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IAdvancedImageからMatへの変換中にエラーが発生しました");
                throw new OcrProcessingException("画像変換に失敗しました", ex);
            }
        }

        /// <summary>
        /// OpenCVのMatをIAdvancedImageに変換します
        /// </summary>
        public IImage ConvertFromMat(Mat mat, bool disposeSource = false)
        {
            if (mat == null) throw new ArgumentNullException(nameof(mat));
            
            try
            {
                // Matをバイト配列に変換
                var imageBytes = mat.ToBytes(".png");
                
                // バイト配列からIImageを作成
                var result = _imageFactory.CreateFromBytesAsync(imageBytes).GetAwaiter().GetResult();
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MatからIAdvancedImageへの変換中にエラーが発生しました");
                throw new OcrProcessingException("画像変換に失敗しました", ex);
            }
            finally
            {
                // リソース解放
                if (disposeSource)
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
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WindowsOpenCvWrapper));
            }
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                // マネージ/アンマネージリソースの解放
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
