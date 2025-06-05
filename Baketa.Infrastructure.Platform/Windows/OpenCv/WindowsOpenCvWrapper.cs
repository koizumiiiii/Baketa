using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.OCR.TextDetection;

namespace Baketa.Infrastructure.Platform.Windows.OpenCv;

    /// <summary>
    /// Windows プラットフォーム用 OpenCV ラッパーの実装
    /// プロジェクト内で使用するが、IOpenCvWrapperインターフェースは直接実装しない
    /// </summary>
    public class WindowsOpenCvWrapper : IDisposable
    {
        private bool _disposed;
        // OpenCVライブラリとの実際のインタラクションを処理する内部クラス/メソッド
        private readonly IWindowsOpenCvLibrary _openCvLib;

        public WindowsOpenCvWrapper(IWindowsOpenCvLibrary openCvLib)
        {
            _openCvLib = openCvLib ?? throw new ArgumentNullException(nameof(openCvLib));
        }
        
        /// <summary>
        /// 画像をグレースケールに変換します
        /// </summary>
        public static async Task<IAdvancedImage> ConvertToGrayscaleAsync(IAdvancedImage source)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(source));
            
            try {
                return source.IsGrayscale ? source : await Task.FromResult(source).ConfigureAwait(false);
            }
            catch (Exception ex) {
                throw new Baketa.Infrastructure.Platform.Windows.OpenCv.Exceptions.OcrProcessingException("グレースケール変換中にエラーが発生しました", ex);
            }
        }

        /// <summary>
        /// 画像に閾値処理を適用します
        /// </summary>
        public async Task<IAdvancedImage> ApplyThresholdAsync(
            IAdvancedImage source,
            double threshold,
            double maxValue,
            Baketa.Core.Abstractions.Imaging.ThresholdType type)
        {
            ArgumentNullException.ThrowIfNull(source);

            var result = await _openCvLib.ThresholdAsync(
                source,
                threshold,
                maxValue,
                ConvertToOpenCvThresholdType(type)).ConfigureAwait(false);

            return result;
        }
        
        /// <summary>
        /// 画像に適応的閾値処理を適用します
        /// </summary>
        public async Task<IAdvancedImage> ApplyAdaptiveThresholdAsync(
            IAdvancedImage source,
            double maxValue,
            Baketa.Core.Abstractions.Imaging.AdaptiveThresholdType adaptiveMethod,
            Baketa.Core.Abstractions.Imaging.ThresholdType thresholdType,
            int blockSize,
            double c)
        {
            ArgumentNullException.ThrowIfNull(source);

            var result = await _openCvLib.AdaptiveThresholdAsync(
                source,
                maxValue,
                ConvertToOpenCvAdaptiveThresholdType(adaptiveMethod),
                ConvertToOpenCvThresholdType(thresholdType),
                blockSize,
                c).ConfigureAwait(false);

            return result;
        }

        /// <summary>
        /// 画像にモルフォロジー演算を適用します
        /// </summary>
        public async Task<IAdvancedImage> ApplyMorphologyAsync(
            IAdvancedImage source,
            Baketa.Core.Abstractions.Imaging.MorphType morphType,
            Size kernelSize,
            int iterations = 1)
        {
            ArgumentNullException.ThrowIfNull(source);

            var result = await _openCvLib.MorphologyAsync(
                source,
                ConvertToOpenCvMorphType(morphType),
                kernelSize.Width).ConfigureAwait(false);

            return result;
        }
        
        /// <summary>
        /// 画像にガウシアンブラーを適用します
        /// </summary>
        public static async Task<IAdvancedImage> ApplyGaussianBlurAsync(
            IAdvancedImage source, 
            System.Drawing.Size kernelSize, 
            double sigmaX = 0, 
            double sigmaY = 0)
        {
            ArgumentNullException.ThrowIfNull(source);
            
            // 実装はスタブ化されています
            return await Task.FromResult(source).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 画像にメディアンブラーを適用します
        /// </summary>
        public static async Task<IAdvancedImage> ApplyMedianBlurAsync(
            IAdvancedImage source, 
            int kernelSize)
        {
            ArgumentNullException.ThrowIfNull(source);
            
            // 実装はスタブ化されています
            return await Task.FromResult(source).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 画像に対してCannyエッジ検出を適用します
        /// </summary>
        public static async Task<IAdvancedImage> ApplyCannyEdgeAsync(
            IAdvancedImage source, 
            double threshold1, 
            double threshold2, 
            int apertureSize = 3, 
            bool l2Gradient = false)
        {
            ArgumentNullException.ThrowIfNull(source);
            
            // 実装はスタブ化されています
            return await Task.FromResult(source).ConfigureAwait(false);
        }
        
        /// <summary>
        /// MSER領域を検出します
        /// </summary>
        public IReadOnlyList<Point[]> DetectMSERRegions(
            IAdvancedImage image, 
            Dictionary<string, object> parameters)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(parameters);
            
            // ディスカード変数を使用してデフォルト値を取得 (迅速化などで将来利用可能性がある値を処理)
            _ = parameters.TryGetValue("delta", out var deltaObj) && deltaObj is int deltaVal ? deltaVal : 5;
            _ = parameters.TryGetValue("minArea", out var minAreaObj) && minAreaObj is int minAreaVal ? minAreaVal : 60;
            _ = parameters.TryGetValue("maxArea", out var maxAreaObj) && maxAreaObj is int maxAreaVal ? maxAreaVal : 14400;
            
            return [];
        }
        
        /// <summary>
        /// ポイント配列に基づくバウンディングボックスを取得します
        /// </summary>
        public static Rectangle GetBoundingRect(Point[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            
            if (points.Length == 0)
                return Rectangle.Empty;
            
            // 最小、最大値を定めて矩形を返す
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            
            foreach (var p in points)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }
            
            return new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }
        
        /// <summary>
        /// Cannyエッジ検出を実行します
        /// </summary>
        public static IAdvancedImage CannyEdgeDetection(IAdvancedImage image, double threshold1, double threshold2)
        {
            ArgumentNullException.ThrowIfNull(image);
            
            // 実装はスタブ化されています
            return image;
        }
        
        /// <summary>
        /// ストローク幅変換を適用します
        /// </summary>
        public static IAdvancedImage StrokeWidthTransform(
            IAdvancedImage grayImage, 
            IAdvancedImage edgeImage, 
            float minStrokeWidth, 
            float maxStrokeWidth)
        {
            ArgumentNullException.ThrowIfNull(grayImage);
            ArgumentNullException.ThrowIfNull(edgeImage);
            
            // 実装はスタブ化されています
            return grayImage;
        }
        
        /// <summary>
        /// ストローク幅の分散を計算します
        /// </summary>
        public static float CalculateStrokeWidthVariance(IAdvancedImage swtImage, Point[] region)
        {
            ArgumentNullException.ThrowIfNull(swtImage);
            ArgumentNullException.ThrowIfNull(region);
            
            // 実装はスタブ化されています
            return 1.0f;
        }
        
        /// <summary>
        /// 平均ストローク幅を計算します
        /// </summary>
        public static float CalculateMeanStrokeWidth(IAdvancedImage swtImage, Point[] region)
        {
            ArgumentNullException.ThrowIfNull(swtImage);
            ArgumentNullException.ThrowIfNull(region);
            
            // 実装はスタブ化されています
            return 5.0f;
        }
        
        /// <summary>
        /// 画像に適応的二値化を適用します
        /// </summary>
        public static IAdvancedImage AdaptiveThreshold(IAdvancedImage image, int blockSize, double c)
        {
            ArgumentNullException.ThrowIfNull(image);
            
            // 実装はスタブ化されています
            return image;
        }
        
        /// <summary>
        /// 画像にガウシアンぼかしを適用します
        /// </summary>
        public static IAdvancedImage GaussianBlur(IAdvancedImage image, int kernelSize, double sigma)
        {
            ArgumentNullException.ThrowIfNull(image);
            
            // 実装はスタブ化されています
            return image;
        }
        
        /// <summary>
        /// モルフォロジー演算を適用します
        /// </summary>
        public static IAdvancedImage MorphologyEx(IAdvancedImage image, Baketa.Core.Abstractions.Imaging.MorphologyOperation operation, int kernelSize)
        {
            ArgumentNullException.ThrowIfNull(image);
            
            // 実装はスタブ化されています
            return image;
        }
        
        /// <summary>
        /// 連結成分を抽出します
        /// </summary>
        public static IReadOnlyList<Point[]> ExtractConnectedComponents(
            IAdvancedImage image, 
            int minComponentSize, 
            int maxComponentSize)
        {
            ArgumentNullException.ThrowIfNull(image);
            
            // スタブ実装
            return [];
        }
        
        /// <summary>
        /// OCR処理用の閾値処理を適用します
        /// </summary>
        public async Task<IAdvancedImage> ApplyOcrThresholdAsync(
            IAdvancedImage source,
            double threshold,
            double maxValue,
            Baketa.Core.Abstractions.OCR.ThresholdType type)
        {
            ArgumentNullException.ThrowIfNull(source);

            // OCR用のThresholdTypeをImaging用に変換する
            var imagingThresholdType = ConvertOcrToImagingThresholdType(type);
            var result = await _openCvLib.ThresholdAsync(
                source,
                threshold,
                maxValue,
                ConvertToOpenCvThresholdType(imagingThresholdType)).ConfigureAwait(false);

            return result;
        }

        /// <summary>
        /// OCR処理用の適応的閾値処理を適用します
        /// </summary>
        public async Task<IAdvancedImage> ApplyOcrAdaptiveThresholdAsync(
            IAdvancedImage source,
            double maxValue,
            Baketa.Core.Abstractions.OCR.AdaptiveThresholdType adaptiveMethod,
            Baketa.Core.Abstractions.OCR.ThresholdType thresholdType,
            int blockSize,
            double c)
        {
            ArgumentNullException.ThrowIfNull(source);

            // OCR用の型をImaging用の型に変換
            var imagingAdaptiveMethod = ConvertOcrToImagingAdaptiveThresholdType(adaptiveMethod);
            var imagingThresholdType = ConvertOcrToImagingThresholdType(thresholdType);
            
            var result = await _openCvLib.AdaptiveThresholdAsync(
                source,
                maxValue,
                ConvertToOpenCvAdaptiveThresholdType(imagingAdaptiveMethod),
                ConvertToOpenCvThresholdType(imagingThresholdType),
                blockSize,
                c).ConfigureAwait(false);

            return result;
        }
        
        /// <summary>
        /// 連結コンポーネントを抽出します（OCR用）
        /// </summary>
        public IReadOnlyList<Point[]> ExtractOcrConnectedComponents(
            IAdvancedImage image,
            int minComponentSize,
            int maxComponentSize)
        {
            ArgumentNullException.ThrowIfNull(image);

            // OpenCV による連結コンポーネント抽出処理
            var components = _openCvLib.FindConnectedComponents(
                image,
                minComponentSize,
                maxComponentSize);

            return components;
        }
        
        /// <summary>
        /// Cannyエッジ検出を実行します (OCR用)
        /// </summary>
        public static IAdvancedImage CannyEdgeDetection(IAdvancedImage image, int threshold1, int threshold2)
        {
            ArgumentNullException.ThrowIfNull(image);
            
            // 実装はスタブ化
            return image;
        }

        /// <summary>
        /// MSERアルゴリズムを使用してテキスト領域を検出します
        /// </summary>
        public IReadOnlyList<Baketa.Core.Abstractions.OCR.TextDetection.TextRegion> DetectTextRegionsWithMser(
            IAdvancedImage image,
            int delta,
            int minArea,
            int maxArea)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WindowsOpenCvWrapper), "このオブジェクトは既に破棄されています。");
            ArgumentNullException.ThrowIfNull(image, nameof(image));

            // MSERによるテキスト領域検出の実装
            var regions = _openCvLib.DetectMserRegions(
                image,
                delta,
                minArea,
                maxArea);

            // 検出結果をTextRegionに変換
            var textRegions = new List<Baketa.Core.Abstractions.OCR.TextDetection.TextRegion>([]);
            foreach (var region in regions)
            {
                textRegions.Add(new Baketa.Core.Abstractions.OCR.TextDetection.TextRegion
                {
                    Bounds = region.Bounds,
                    Contour = region.Points,
                    ConfidenceScore = region.Confidence
                });
            }

            return textRegions;
        }

        /// <summary>
        /// SWTアルゴリズムを使用してテキスト領域を検出します
        /// </summary>
        public IReadOnlyList<Baketa.Core.Abstractions.OCR.TextDetection.TextRegion> DetectTextRegionsWithSwt(
            IAdvancedImage image,
            bool darkTextOnLight,
            float strokeWidthRatio)
        {
            ArgumentNullException.ThrowIfNull(image);

            // SWTによるテキスト領域検出の実装
            var regions = _openCvLib.DetectSwtRegions(
                image,
                darkTextOnLight,
                strokeWidthRatio);

            // 検出結果をTextRegionに変換
            var textRegions = new List<Baketa.Core.Abstractions.OCR.TextDetection.TextRegion>([]);
            foreach (var region in regions)
            {
                textRegions.Add(new Baketa.Core.Abstractions.OCR.TextDetection.TextRegion
                {
                    Bounds = region.Bounds,
                    Contour = region.Points,
                    ConfidenceScore = region.Confidence,
                    // SWT特有の追加情報
                    Metadata =
                    {
                        ["MeanStrokeWidth"] = region.StrokeWidth,
                        ["StrokeDirection"] = darkTextOnLight ? "DarkOnLight" : "LightOnDark"
                    }
                });
            }

            return textRegions;
        }

        #region 型変換ヘルパーメソッド

        // ThresholdType から OpenCV の閾値タイプへの変換
        private static int ConvertToOpenCvThresholdType(Baketa.Core.Abstractions.Imaging.ThresholdType type)
        {
            return type switch
            {
                Baketa.Core.Abstractions.Imaging.ThresholdType.Binary => 0, // OpenCV ThresholdType.Binary
                Baketa.Core.Abstractions.Imaging.ThresholdType.BinaryInv => 1, // OpenCV ThresholdType.BinaryInv
                Baketa.Core.Abstractions.Imaging.ThresholdType.Trunc => 2, // OpenCV ThresholdType.Trunc
                Baketa.Core.Abstractions.Imaging.ThresholdType.ToZero => 3, // OpenCV ThresholdType.ToZero
                Baketa.Core.Abstractions.Imaging.ThresholdType.ToZeroInv => 4, // OpenCV ThresholdType.ToZeroInv
                Baketa.Core.Abstractions.Imaging.ThresholdType.Otsu => 8, // OpenCV ThresholdType.Otsu
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        // AdaptiveThresholdType から OpenCV の適応的閾値タイプへの変換
        private static int ConvertToOpenCvAdaptiveThresholdType(Baketa.Core.Abstractions.Imaging.AdaptiveThresholdType type)
        {
            return type switch
            {
                Baketa.Core.Abstractions.Imaging.AdaptiveThresholdType.MeanC => 0, // OpenCV AdaptiveThresholdType.Mean
                Baketa.Core.Abstractions.Imaging.AdaptiveThresholdType.GaussianC => 1, // OpenCV AdaptiveThresholdType.Gaussian
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        // MorphType から OpenCV のモルフォロジー演算タイプへの変換
        private static int ConvertToOpenCvMorphType(Baketa.Core.Abstractions.Imaging.MorphType type)
        {
            return type switch
            {
                Baketa.Core.Abstractions.Imaging.MorphType.Erode => 0, // OpenCV MorphTypes.Erode
                Baketa.Core.Abstractions.Imaging.MorphType.Dilate => 1, // OpenCV MorphTypes.Dilate
                Baketa.Core.Abstractions.Imaging.MorphType.Open => 2, // OpenCV MorphTypes.Open
                Baketa.Core.Abstractions.Imaging.MorphType.Close => 3, // OpenCV MorphTypes.Close
                Baketa.Core.Abstractions.Imaging.MorphType.Gradient => 4, // OpenCV MorphTypes.Gradient
                Baketa.Core.Abstractions.Imaging.MorphType.TopHat => 5, // OpenCV MorphTypes.TopHat
                Baketa.Core.Abstractions.Imaging.MorphType.BlackHat => 6, // OpenCV MorphTypes.BlackHat
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        // OCR.ThresholdType から Imaging.ThresholdType への変換
        private static Baketa.Core.Abstractions.Imaging.ThresholdType ConvertOcrToImagingThresholdType(Baketa.Core.Abstractions.OCR.ThresholdType type)
        {
            return type switch
            {
                Baketa.Core.Abstractions.OCR.ThresholdType.Binary => Baketa.Core.Abstractions.Imaging.ThresholdType.Binary,
                Baketa.Core.Abstractions.OCR.ThresholdType.BinaryInv => Baketa.Core.Abstractions.Imaging.ThresholdType.BinaryInv,
                Baketa.Core.Abstractions.OCR.ThresholdType.Truncate => Baketa.Core.Abstractions.Imaging.ThresholdType.Trunc,
                Baketa.Core.Abstractions.OCR.ThresholdType.ToZero => Baketa.Core.Abstractions.Imaging.ThresholdType.ToZero,
                Baketa.Core.Abstractions.OCR.ThresholdType.ToZeroInv => Baketa.Core.Abstractions.Imaging.ThresholdType.ToZeroInv,
                Baketa.Core.Abstractions.OCR.ThresholdType.Otsu => Baketa.Core.Abstractions.Imaging.ThresholdType.Otsu,
                Baketa.Core.Abstractions.OCR.ThresholdType.Adaptive => Baketa.Core.Abstractions.Imaging.ThresholdType.Binary, // 適応的閾値処理の場合はバイナリをデフォルトに
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        // OCR.AdaptiveThresholdType から Imaging.AdaptiveThresholdType への変換
        private static Baketa.Core.Abstractions.Imaging.AdaptiveThresholdType ConvertOcrToImagingAdaptiveThresholdType(Baketa.Core.Abstractions.OCR.AdaptiveThresholdType type)
        {
            return type switch
            {
                Baketa.Core.Abstractions.OCR.AdaptiveThresholdType.Mean => Baketa.Core.Abstractions.Imaging.AdaptiveThresholdType.MeanC,
                Baketa.Core.Abstractions.OCR.AdaptiveThresholdType.Gaussian => Baketa.Core.Abstractions.Imaging.AdaptiveThresholdType.GaussianC,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        #endregion
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放する場合はtrue</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // マネージドリソースの解放
                    (_openCvLib as IDisposable)?.Dispose();
                }
                
                // アンマネージドリソースの解放（該当なし）
                
                _disposed = true;
            }
        }
        
        /// <summary>
        /// ファイナライザ
        /// </summary>
        ~WindowsOpenCvWrapper()
        {
            Dispose(false);
        }
    }
