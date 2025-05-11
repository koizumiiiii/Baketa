using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Infrastructure.Platform.Windows.OpenCv
{
    /// <summary>
    /// WindowsOpenCvWrapperをIOpenCvWrapperインターフェースに適応させるアダプタークラス
    /// </summary>
    public class OpenCvWrapperAdapter : Baketa.Core.Abstractions.Imaging.IOpenCvWrapper
    {
        private readonly WindowsOpenCvWrapper _wrapper;
        private bool _disposed;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="wrapper">WindowsOpenCvWrapperインスタンス</param>
        public OpenCvWrapperAdapter(WindowsOpenCvWrapper wrapper)
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
        public Task<IAdvancedImage> ApplyThresholdAsync(IAdvancedImage source, double threshold, double maxValue, ThresholdType type)
        {
            return _wrapper.ApplyThresholdAsync(source, threshold, maxValue, type);
        }
        
        /// <summary>
        /// 画像に適応的閾値処理を適用します
        /// </summary>
        public Task<IAdvancedImage> ApplyAdaptiveThresholdAsync(IAdvancedImage source, double maxValue, AdaptiveThresholdType adaptiveType, ThresholdType thresholdType, int blockSize, double c)
        {
            return _wrapper.ApplyAdaptiveThresholdAsync(source, maxValue, adaptiveType, thresholdType, blockSize, c);
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
        public Task<IAdvancedImage> ApplyMorphologyAsync(IAdvancedImage source, MorphType morphType, Size kernelSize, int iterations = 1)
        {
            return _wrapper.ApplyMorphologyAsync(source, morphType, kernelSize, iterations);
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
        public IAdvancedImage CannyEdgeDetection(IAdvancedImage image, double threshold1, double threshold2)
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
        return WindowsOpenCvWrapper.ExtractConnectedComponents(image, minComponentSize, maxComponentSize);
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
        /// 画像に適応的二値化を適用します
        /// </summary>
        public IAdvancedImage AdaptiveThreshold(IAdvancedImage image, int blockSize, double c)
        {
        return WindowsOpenCvWrapper.AdaptiveThreshold(image, blockSize, c);
        }
        
        /// <summary>
        /// 画像にガウシアンぼかしを適用します
        /// </summary>
        public IAdvancedImage GaussianBlur(IAdvancedImage image, int kernelSize, double sigma)
        {
        return WindowsOpenCvWrapper.GaussianBlur(image, kernelSize, sigma);
        }
        
        /// <summary>
        /// モルフォロジー演算を適用します
        /// </summary>
        public IAdvancedImage MorphologyEx(IAdvancedImage image, MorphologyOperation operation, int kernelSize)
        {
        return WindowsOpenCvWrapper.MorphologyEx(image, operation, kernelSize);
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
        ~OpenCvWrapperAdapter()
        {
            Dispose(false);
        }
    }
}