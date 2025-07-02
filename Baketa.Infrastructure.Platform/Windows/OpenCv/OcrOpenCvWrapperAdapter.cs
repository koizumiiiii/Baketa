using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.OCR.TextDetection;

namespace Baketa.Infrastructure.Platform.Windows.OpenCv;

    /// <summary>
    /// WindowsOpenCvWrapperをOCR.IOpenCvWrapperインターフェースに適応させるアダプタークラス
    /// </summary>
    public class OcrOpenCvWrapperAdapter : Baketa.Core.Abstractions.OCR.IOpenCvWrapper
    {
        private readonly WindowsOpenCvWrapper _wrapper;
        private bool _disposed;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="wrapper">WindowsOpenCvWrapperインスタンス</param>
        public OcrOpenCvWrapperAdapter(WindowsOpenCvWrapper wrapper)
        {
            _wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
        }
        
        /// <summary>
        /// 画像をグレースケールに変換します
        /// </summary>
        public Task<IAdvancedImage> ConvertToGrayscaleAsync(IAdvancedImage source)
        {
            return WindowsOpenCvWrapper.ConvertToGrayscaleAsync(source);
        }
        
        /// <summary>
        /// 画像に閾値処理を適用します
        /// </summary>
        public Task<IAdvancedImage> ApplyThresholdAsync(
            IAdvancedImage source, 
            double threshold, 
            double maxValue, 
            Baketa.Core.Abstractions.OCR.ThresholdType type)
        {
            return _wrapper.ApplyOcrThresholdAsync(source, threshold, maxValue, type);
        }
        
        /// <summary>
        /// 画像に適応的閾値処理を適用します
        /// </summary>
        public Task<IAdvancedImage> ApplyAdaptiveThresholdAsync(
            IAdvancedImage source,
            double maxValue,
            Baketa.Core.Abstractions.OCR.AdaptiveThresholdType adaptiveType,
            Baketa.Core.Abstractions.OCR.ThresholdType thresholdType,
            int blockSize,
            double c)
        {
            return _wrapper.ApplyOcrAdaptiveThresholdAsync(source, maxValue, adaptiveType, thresholdType, blockSize, c);
        }
        
        /// <summary>
        /// 画像にガウシアンブラーを適用します
        /// </summary>
        public Task<IAdvancedImage> ApplyGaussianBlurAsync(IAdvancedImage source, Size kernelSize, double sigmaX = 0, double sigmaY = 0)
        {
            return WindowsOpenCvWrapper.ApplyGaussianBlurAsync(source, kernelSize, sigmaX, sigmaY);
        }
        
        /// <summary>
        /// 画像にメディアンブラーを適用します
        /// </summary>
        public Task<IAdvancedImage> ApplyMedianBlurAsync(IAdvancedImage source, int kernelSize)
        {
            return WindowsOpenCvWrapper.ApplyMedianBlurAsync(source, kernelSize);
        }
        
        /// <summary>
        /// 画像に対してCannyエッジ検出を適用します
        /// </summary>
        public Task<IAdvancedImage> ApplyCannyEdgeAsync(IAdvancedImage source, double threshold1, double threshold2, int apertureSize = 3, bool l2Gradient = false)
        {
            return WindowsOpenCvWrapper.ApplyCannyEdgeAsync(source, threshold1, threshold2, apertureSize, l2Gradient);
        }
        
        /// <summary>
        /// 画像にモルフォロジー演算を適用します
        /// </summary>
        public Task<IAdvancedImage> ApplyMorphologyAsync(
            IAdvancedImage source,
            Baketa.Core.Abstractions.OCR.MorphType morphType,
            Size kernelSize,
            int iterations = 1)
        {
            // OCR用のMorphTypeをImagingのMorphTypeに変換
            var imagingMorphType = (Baketa.Core.Abstractions.Imaging.MorphType)Enum.Parse(
                typeof(Baketa.Core.Abstractions.Imaging.MorphType), 
                morphType.ToString());
                
            return _wrapper.ApplyMorphologyAsync(source, imagingMorphType, kernelSize, iterations);
        }
        
        /// <summary>
        /// MSER領域を検出します
        /// </summary>
        public IReadOnlyList<Point[]> DetectMSERRegions(IAdvancedImage image, Dictionary<string, object> parameters)
        {
            return _wrapper.DetectMSERRegions(image, parameters);
        }
        
        /// <summary>
        /// ポイント配列に基づくバウンディングボックスを取得します
        /// </summary>
        public Rectangle GetBoundingRect(Point[] points)
        {
            return WindowsOpenCvWrapper.GetBoundingRect(points);
        }
        
        /// <summary>
        /// Cannyエッジ検出を実行します
        /// </summary>
        public IAdvancedImage CannyEdgeDetection(IAdvancedImage image, int threshold1, int threshold2)
        {
            return WindowsOpenCvWrapper.CannyEdgeDetection(image, threshold1, threshold2);
        }
        
        /// <summary>
        /// ストローク幅変換を適用します
        /// </summary>
        public IAdvancedImage StrokeWidthTransform(IAdvancedImage grayImage, IAdvancedImage edgeImage, float minStrokeWidth, float maxStrokeWidth)
        {
            return WindowsOpenCvWrapper.StrokeWidthTransform(grayImage, edgeImage, minStrokeWidth, maxStrokeWidth);
        }
        
        /// <summary>
        /// 連結成分を抽出します
        /// </summary>
        public IReadOnlyList<Point[]> ExtractConnectedComponents(IAdvancedImage image, int minComponentSize, int maxComponentSize)
        {
            return _wrapper.ExtractOcrConnectedComponents(image, minComponentSize, maxComponentSize);
        }
        
        /// <summary>
        /// ストローク幅の分散を計算します
        /// </summary>
        public float CalculateStrokeWidthVariance(IAdvancedImage swtImage, Point[] region)
        {
            return WindowsOpenCvWrapper.CalculateStrokeWidthVariance(swtImage, region);
        }
        
        /// <summary>
        /// 平均ストローク幅を計算します
        /// </summary>
        public float CalculateMeanStrokeWidth(IAdvancedImage swtImage, Point[] region)
        {
        return WindowsOpenCvWrapper.CalculateMeanStrokeWidth(swtImage, region);
        }
        
        /// <summary>
        /// テキスト領域をMSERアルゴリズムを使用して検出します
        /// </summary>
        public IReadOnlyList<Baketa.Core.Abstractions.OCR.TextDetection.TextRegion> DetectTextRegionsWithMser(IAdvancedImage image, int delta, int minArea, int maxArea)
        {
            return _wrapper.DetectTextRegionsWithMser(image, delta, minArea, maxArea);
        }
        
        /// <summary>
        /// テキスト領域をSWTアルゴリズムを使用して検出します
        /// </summary>
        public IReadOnlyList<Baketa.Core.Abstractions.OCR.TextDetection.TextRegion> DetectTextRegionsWithSwt(IAdvancedImage image, bool darkTextOnLight, float strokeWidthRatio)
        {
            return _wrapper.DetectTextRegionsWithSwt(image, darkTextOnLight, strokeWidthRatio);
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
                    _wrapper.Dispose();
                }
                
                // アンマネージドリソースの解放（該当なし）
                
                _disposed = true;
            }
        }
        
        /// <summary>
        /// ファイナライザ
        /// </summary>
        ~OcrOpenCvWrapperAdapter()
        {
            Dispose(false);
        }
    }
