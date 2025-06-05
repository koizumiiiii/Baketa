using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Services;

    /// <summary>
    /// OCR (光学式文字認識) サービスインターフェース
    /// </summary>
    public interface IOcrService
    {
        /// <summary>
        /// 画像からテキストを認識します
        /// </summary>
        /// <param name="image">処理する画像</param>
        /// <returns>認識されたテキスト</returns>
        Task<string> RecognizeTextAsync(IImage image);
        
        /// <summary>
        /// 画像の特定領域からテキストを認識します
        /// </summary>
        /// <param name="image">処理する画像</param>
        /// <param name="region">処理する領域</param>
        /// <returns>認識されたテキスト</returns>
        Task<string> RecognizeTextInRegionAsync(IImage image, Rectangle region);
        
        /// <summary>
        /// 画像からテキスト領域を検出します
        /// </summary>
        /// <param name="image">処理する画像</param>
        /// <returns>テキスト領域のリスト</returns>
        Task<List<TextRegion>> DetectTextRegionsAsync(IImage image);
        
        /// <summary>
        /// 画像からテキスト領域とそのテキストを認識します
        /// </summary>
        /// <param name="image">処理する画像</param>
        /// <returns>テキスト領域と認識されたテキストのリスト</returns>
        Task<List<TextRegion>> RecognizeTextRegionsAsync(IImage image);
        
        /// <summary>
        /// OCR処理のために画像を最適化します
        /// </summary>
        /// <param name="image">元の画像</param>
        /// <returns>OCR処理に最適化された画像</returns>
        Task<IImage> OptimizeImageForOcrAsync(IImage image);
        
        /// <summary>
        /// OCR設定を取得します
        /// </summary>
        /// <returns>OCR設定</returns>
        OcrSettings GetSettings();
        
        /// <summary>
        /// OCR設定を設定します
        /// </summary>
        /// <param name="settings">OCR設定</param>
        void SetSettings(OcrSettings settings);
    }
    
    /// <summary>
    /// テキスト領域情報
    /// </summary>
    public class TextRegion
    {
        /// <summary>
        /// 領域の位置と大きさ
        /// </summary>
        public Rectangle Bounds { get; set; }
        
        /// <summary>
        /// 認識されたテキスト
        /// </summary>
        public string? Text { get; set; }
        
        /// <summary>
        /// 認識の信頼度 (0.0-1.0)
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// テキストの向き
        /// </summary>
        public TextOrientation Orientation { get; set; }
    }
    
    /// <summary>
    /// テキストの向き
    /// </summary>
    public enum TextOrientation
    {
        /// <summary>
        /// 水平
        /// </summary>
        Horizontal = 0,
        
        /// <summary>
        /// 垂直
        /// </summary>
        Vertical = 1,
        
        /// <summary>
        /// 右90度回転
        /// </summary>
        Rotated90 = 2,
        
        /// <summary>
        /// 180度回転
        /// </summary>
        Rotated180 = 3,
        
        /// <summary>
        /// 不明
        /// </summary>
        Unknown = 255
    }
    
    /// <summary>
    /// OCR設定
    /// </summary>
    public class OcrSettings
    {
        /// <summary>
        /// 言語
        /// </summary>
        public string Language { get; set; } = "ja";
        
        /// <summary>
        /// 処理前に画像の最適化を行うかどうか
        /// </summary>
        public bool PreprocessImage { get; set; } = true;
        
        /// <summary>
        /// 最小信頼度 (0.0-1.0)
        /// </summary>
        public float MinimumConfidence { get; set; } = 0.6f;
        
        /// <summary>
        /// テキスト向きの自動検出
        /// </summary>
        public bool AutoDetectOrientation { get; set; } = true;
        
        /// <summary>
        /// スケール係数
        /// </summary>
        public float ScaleFactor { get; set; } = 1.0f;
        
        /// <summary>
        /// 使用するOCRエンジン
        /// </summary>
        public OcrEngine Engine { get; set; } = OcrEngine.PaddleOcr;
    }
    
    /// <summary>
    /// OCRエンジン
    /// </summary>
    public enum OcrEngine
    {
        /// <summary>
        /// PaddleOCR
        /// </summary>
        PaddleOcr = 0,
        
        /// <summary>
        /// Tesseract
        /// </summary>
        Tesseract = 1,
        
        /// <summary>
        /// Windows OCR
        /// </summary>
        WindowsOcr = 2,
        
        /// <summary>
        /// カスタムOCR
        /// </summary>
        Custom = 3
    }
