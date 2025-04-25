using System.Drawing;

namespace Baketa.Core.Models.OCR
{
    /// <summary>
    /// OCR処理の結果を表すクラス
    /// </summary>
    public class OcrResult
    {
        /// <summary>
        /// 検出されたテキスト
        /// </summary>
        public string Text { get; }
        
        /// <summary>
        /// テキストの位置と範囲
        /// </summary>
        public Rectangle Bounds { get; }
        
        /// <summary>
        /// 信頼度スコア (0.0〜1.0)
        /// </summary>
        public float Confidence { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="text">検出されたテキスト</param>
        /// <param name="bounds">テキストの位置と範囲</param>
        /// <param name="confidence">信頼度スコア</param>
        public OcrResult(string text, Rectangle bounds, float confidence)
        {
            Text = text;
            Bounds = bounds;
            Confidence = confidence;
        }
    }
}